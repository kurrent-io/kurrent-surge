using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Variant;
using KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Processors.Gateway;

sealed class SubscriptionSource : ClientModuleBase, ISubscriptionSource {
    readonly StreamsClient              _streams;
    readonly ProcessorOptions           _settings;
    readonly TimeProvider               _timeProvider;
    readonly CheckpointStore            _checkpointStore;
    readonly PartitionedPositionTracker _tracker;
    readonly HybridLogicalClock         _clock;
    readonly KurrentClient              _client;
    readonly bool                       _ownsClient;

    readonly Dictionary<string, PendingPositionsFlush> _pendingFlushes = new();

    // ownsClient is an internal concern — drones built by the runner always own their client.
    // Integration tests pass ownsClient: false when sharing a fixture-scoped client across tests.
    internal SubscriptionSource(
        KurrentClient client,
        ProcessorOptions settings,
        TimeProvider timeProvider,
        bool ownsClient = true
    ) : base(client) {
        _streams      = new StreamsClient(client);
        _settings     = settings;
        _timeProvider = timeProvider;
        _client       = client;
        _ownsClient   = ownsClient;
        _clock        = new HybridLogicalClock(timeProvider);
        _tracker      = new PartitionedPositionTracker();

        _checkpointStore = new CheckpointStore(
            settings.ProcessorId,
            settings.MaxParallelism,
            settings.Checkpoint.RetentionCount,
            client.Streams,
            TypeMapper
        );
    }

    #region ->> Subscribe <<-

    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var partitions = Enumerable.Range(0, _settings.MaxParallelism)
            .Select(Partition.From)
            .ToArray();

        RecordPosition startPosition;

        if (_settings.Subscription.StartPosition is not { } explicitStart) {
            var checkpoint = await _checkpointStore
                .LoadLatestAsync(partitions, _settings.Subscription.InitialPosition, ct)
                .ConfigureAwait(false);

            startPosition = checkpoint.LowWatermark;

            var positions = checkpoint.Positions
                .ToImmutableDictionary(p => p.Partition, p => p.Position);

            _tracker.Reset(positions);
        }
        else {
            if (explicitStart.Stream.IsUnset) {
                var record = await _streams
                    .InspectRecord(explicitStart.LogPosition, ct)
                    .ConfigureAwait(false);

                explicitStart = RecordPosition.From(
                    record.Position.LogPosition,
                    record.Stream,
                    record.Revision);
            }

            await _checkpointStore
                .ResetAsync(
                    _settings.DroneId,
                    CheckpointId.From(_clock.Now()),
                    explicitStart,
                    ct
                )
                .ConfigureAwait(false);

            startPosition = _settings.Subscription.InitialPosition == SubscriptionInitialPosition.Earliest
                ? RecordPosition.Earliest
                : RecordPosition.Latest;

            var positions = ImmutableDictionary<Partition, RecordPosition>.Empty
                .Add(Partition.From(0), startPosition);

            _tracker.Reset(positions);
        }

        var channel = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(_settings.BufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        StartSubscriptionPump(startPosition, channel.Writer, ct);

        await foreach (var msg in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            // Snapshot positions at yield time for checkpoint messages
            if (msg.Value is Checkpoint trigger && trigger.Positions.Count == 0) {
                yield return SnapshotCheckpointMessage(trigger.Trigger);
            }
            // Calculate partition for records before yielding
            else if (msg.Value is Record record) {
                var partition = PartitionCalculator.XXHash
                    .Calculate(record.Stream, _settings.MaxParallelism);

                yield return record with {
                    Position = record.Position with { Partition = Partition.From(partition) }
                };
            }
            else {
                yield return msg;
            }
        }

        void StartSubscriptionPump(RecordPosition from, ChannelWriter<SubscribeMessage> writer, CancellationToken pumpCt) {
            _ = Task.Run(async () => {
                try {
                    var subscription = _streams.Subscribe(
                        SerializerProvider, MetadataDecoder,
                        _settings.Subscription.Filter, from,
                        _settings.SkipDecoding, pumpCt);

                    var connectedHandled = false;

                    await foreach (var msg in subscription.ConfigureAwait(false)) {
                        await writer.WriteAsync(msg, pumpCt).ConfigureAwait(false);

                        if (!connectedHandled && msg.Value is Connected) {
                            connectedHandled = true;
                            StartFlushTimer(writer, pumpCt);
                        }
                    }

                    writer.TryComplete();
                }
                catch (OperationCanceledException) {
                    writer.TryComplete();
                }
                catch (Exception ex) {
                    writer.TryComplete(ex);
                }
            }, pumpCt);
        }

        void StartFlushTimer(ChannelWriter<SubscribeMessage> writer, CancellationToken timerCt) {
            _ = Task.Run(async () => {
                using var timer = new PeriodicTimer(_settings.Checkpoint.Interval);

                try {
                    while (await timer.WaitForNextTickAsync(timerCt).ConfigureAwait(false)) {
                        // Write a trigger marker with empty positions — will be filled at yield time
                        var trigger = CreateCheckpointTrigger(CheckpointTrigger.Interval);
                        await writer.WriteAsync(trigger, timerCt).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) {
                    // Expected on shutdown
                }
            }, timerCt);
        }
    }

    #endregion

    #region ->> Track & Checkpoint <<-

    public void Track(Record record) => _tracker.Track(record);

    public async ValueTask<PartitionPositions> CompleteCheckpointAsync(
        string checkpointId, CancellationToken ct = default
    ) {
        if (!_pendingFlushes.Remove(checkpointId, out var pendingFlush))
            return PartitionPositions.Empty;

        PartitionPositions positions = pendingFlush.Positions
            .Where(x => x.Value.LogPosition != LogPosition.Earliest)
            .ToImmutableDictionary();

        if (positions.Count == 0)
            return PartitionPositions.Empty;

        var partitionPositions = positions
            .Select(kvp => new PartitionPosition(kvp.Key, kvp.Value))
            .ToArray();

        await _checkpointStore
            .StoreAsync(_settings.DroneId, checkpointId, partitionPositions, ct)
            .ConfigureAwait(false);

        pendingFlush.Execute();

        return positions;
    }

    public Checkpoint CreateShutdownCheckpoint() =>
        SnapshotCheckpointMessage(CheckpointTrigger.Shutdown);

    #endregion

    #region ->> Internals <<-

    SubscribeMessage CreateCheckpointTrigger(CheckpointTrigger trigger) {
        var now = _clock.Now();

        return new Checkpoint(
            CheckpointId.From(now),
            trigger,
            now.DateTimeOffset,
            PartitionPositions.Empty
        );
    }

    Checkpoint SnapshotCheckpointMessage(CheckpointTrigger trigger) {
        var now          = _clock.Now();
        var checkpointId = CheckpointId.From(now);
        var pendingFlush = _tracker.PrepareFlush();

        _pendingFlushes[checkpointId] = pendingFlush;

        PartitionPositions positions = pendingFlush.Positions
            .Where(x => x.Value.LogPosition != LogPosition.Earliest)
            .ToImmutableDictionary();

        return new Checkpoint(
            checkpointId,
            trigger,
            now.DateTimeOffset,
            positions
        );
    }

    #endregion

    public async ValueTask DisposeAsync() {
        _tracker.Dispose();

        if (_ownsClient)
            await _client.DisposeAsync().ConfigureAwait(false);
    }
}

public readonly record struct Connected(SessionId SessionId);

public readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint> {
    public static readonly SubscribeMessage None;
}

static class InternalClientExtensions {
    /// <summary>
    /// Subscribes to a stream or log via the V1 wire protocol.
    /// Connection phase (first message validation) runs eagerly before any yield,
    /// so errors throw directly to the caller.
    /// </summary>
    public static async IAsyncEnumerable<SubscribeMessage> Subscribe(
        this StreamsClient streams,
        ISchemaSerializerProvider serializerProvider,
        IMetadataDecoder metadataDecoder,
        IRecordFilter filter,
        RecordPosition start,
        bool skipDecoding,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var request = filter.IsStreamFilter
            ? ProtocolMapping.Requests.CreateStreamSubscriptionRequest(filter, start.Revision)
            : ProtocolMapping.Requests.CreateLogSubscriptionRequest(filter, start.LogPosition);

        AsyncServerStreamingCall<ReadResp> grpcSession;
        SessionId sessionId;

        // ── Connection phase ── throws directly to caller, no yield here
        try {
            grpcSession = streams.LegacyServiceClient.Read(request, cancellationToken: ct);

            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            // v1 protocol bull: StreamNotFound is a message, not an exception (unlike 99.9% of other error cases)
            // And we dont know if the stream is missing or deleted until we check the stream info.
            // Meanwhile the stream state might have changed to deleted or tombstoned.
            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await streams.GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    // lets just throw not found if the stream becames active again
                    // and let the user retry. we should do it ourselves in v2
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
                throw new UnreachableException(
                    $"EVERYTHING IS BROKEN! First message must be the subscription confirmation but got: " +
                    $"{grpcSession.ResponseStream.Current?.ContentCase}"
                );

            sessionId = SessionId.From(Guid.Parse(grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId));
        }
        catch (RpcException rex) {
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            // when stream is tombstoned a stream deleted exception is thrown for some weird reason
            if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                throw new LogPositionNotFoundException(position, rex);
            }

            throw rex.AsWellKnownException();
        }

        yield return new Connected(sessionId);

        // ── Pump phase ── yields Record | Checkpoint
        SequenceGenerator sequencer = new();

        try {
            while (await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false)) {
                var resp = grpcSession.ResponseStream.Current;

                switch (resp?.ContentCase) {
                    case ReadResp.ContentOneofCase.Checkpoint:
                        yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext());
                        break;

                    case ReadResp.ContentOneofCase.CaughtUp:
                        yield return resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                        break;

                    case ReadResp.ContentOneofCase.Event:
                        var record = await ProtocolMapping.MapToRecordAsync(
                            resp.Event,
                            sequencer.FetchNext(),
                            serializerProvider, metadataDecoder,
                            SchemaRegistryPolicy.NoRequirements,
                            skipDecoding,
                            ct
                        ).ConfigureAwait(false);

                        // client-side filter
                        // will implement server side in v2
                        if (filter.IsMatch(record))
                            yield return record;

                        break;
                }
            }
        }
        finally {
            grpcSession.Dispose();
        }
    }
}
