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
    readonly ProcessorOptions           _options;
    readonly TimeProvider               _timeProvider;
    readonly CheckpointStore            _checkpointStore;
    readonly PartitionedPositionTracker _tracker;
    readonly HybridLogicalClock         _clock;
    readonly KurrentClient              _client;

    readonly Lock _writeLock = new();

    ChannelWriter<SubscribeMessage> _channelWriter = null!;
    CancellationTokenSource?        _cts;
    Task?                           _pumpTask;
    Task?                           _timerTask;

    bool _shutdownInjected;
    int  _connectedObserved;

    internal SubscriptionSource(
        KurrentClient client,
        ProcessorOptions options,
        TimeProvider timeProvider
    ) : base(client) {
        _streams      = new StreamsClient(client);
        _options      = options;
        _timeProvider = timeProvider;
        _client       = client;
        _clock        = new HybridLogicalClock(timeProvider);
        _tracker      = new PartitionedPositionTracker();

        _checkpointStore = new KurrentDBCheckpointStore(
            options.ProcessorId,
            options.MaxParallelism,
            options.Checkpoint.RetentionCount,
            client.Streams,
            TypeMapper,
            _timeProvider,
            NullLogger<KurrentDBCheckpointStore>.Instance
        );
    }

    #region ->> Subscribe <<-

    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        [EnumeratorCancellation] CancellationToken ct
    ) {
        // Source-owned CTS linked to the caller. Internal tasks (pump, timer, ct
        // registration) hang off _cts.Token so DisposeAsync can shut everything down
        // independently of the host's stoppingToken. Either source of cancellation
        // tears down both internal tasks.
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sourceCt = _cts.Token;

        var partitions = Enumerable.Range(0, _options.MaxParallelism)
            .Select(Partition.From)
            .ToArray();

        RecordPosition startPosition;

        if (_options.Subscription.StartPosition is not { } explicitStart) {
            var checkpoint = await _checkpointStore
                .LoadLatestAsync(partitions, _options.Subscription.InitialPosition, sourceCt)
                .ConfigureAwait(false);

            startPosition = checkpoint.LowWatermark;

            _tracker.Reset(checkpoint.Positions);
        }
        else {
            if (explicitStart.Stream.IsUnset) {
                var record = await _streams
                    .InspectRecord(explicitStart.LogPosition, sourceCt)
                    .ConfigureAwait(false);

                explicitStart = RecordPosition.From(
                    record.Position.LogPosition,
                    record.Stream,
                    record.Revision);
            }

            await _checkpointStore
                .ResetAsync(
                    _options.DroneId,
                    _clock.Now(),
                    explicitStart,
                    sourceCt
                )
                .ConfigureAwait(false);

            startPosition = _options.Subscription.InitialPosition == SubscriptionInitialPosition.Earliest
                ? RecordPosition.Earliest
                : RecordPosition.Latest;

            _tracker.Reset([startPosition with { Partition = Partition.From(0) }]);
        }

        var channel = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(_options.BufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _channelWriter = channel.Writer;

        // Cancellation (host or DisposeAsync) shuts the source down. UnsafeRegister
        // fires synchronously inline with Cancel(), so it always wins the race against
        // the pump's catch path.
        //
        // Pre-Connected cancellation: we fault the channel with an OperationCanceledException
        // instead of injecting a Shutdown marker. Without this guard, OnCheckpoint(Shutdown)
        // would fire before OnConnected ever ran — breaking the documented lifecycle ordering.
        // With the channel faulted, the drone's reader throws, RunAsync catches as cancellation,
        // and InitiateShutdown runs OnComplete (no OnConnected, no OnCheckpoint).
        using var ctRegistration = sourceCt.UnsafeRegister(static state => {
            var src = (SubscriptionSource)state!;
            if (Volatile.Read(ref src._connectedObserved) == 1)
                _ = src.InjectCheckpoint(CheckpointTrigger.Shutdown, "subscription canceled");
            else
                src._channelWriter.TryComplete(new OperationCanceledException("canceled before Connected"));
        }, this);

        _pumpTask = StartSubscriptionPump(startPosition, channel.Writer, sourceCt);

        // Reader uses CancellationToken.None — drains until channel completed (which is
        // done by InjectCheckpoint(Shutdown) under the write lock).
        await foreach (var msg in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
            // Records get partition tagging; Checkpoint markers get their positions
            // stamped at drain time so they reflect the tracker's CURRENT state — not
            // the state at the moment InjectCheckpoint was called. This matters for
            // host-cancellation: by the time the marker is yielded, the drone has
            // finished tracking the in-flight record/batch, so the snapshot includes it.
            if (msg.Value is Record record) {
                var partition = PartitionCalculator.XXHash
                    .Calculate(record.Stream, _options.MaxParallelism);

                yield return record with {
                    Position = record.Position with { Partition = Partition.From(partition) }
                };
            }
            else if (msg.Value is Checkpoint cp) {
                yield return cp with { Positions = SnapshotPositionsLocked() };
            }
            else {
                yield return msg;
            }
        }

        Task StartSubscriptionPump(RecordPosition from, ChannelWriter<SubscribeMessage> writer, CancellationToken pumpCt) {
            return Task.Run(async () => {
                try {
                    var subscription = _streams.Subscribe(
                        SerializerProvider, MetadataDecoder,
                        _options.Subscription.Filter, from,
                        _options.SkipDecoding, pumpCt);

                    await foreach (var msg in subscription.ConfigureAwait(false)) {
                        await writer.WriteAsync(msg, pumpCt).ConfigureAwait(false);

                        // The Connected marker has been written to the channel — from now
                        // on, FIFO ordering guarantees the drone sees Connected (and runs
                        // OnConnected) before any Shutdown marker injected by cancellation.
                        if (Volatile.Read(ref _connectedObserved) == 0 && msg.Value is Connected) {
                            Volatile.Write(ref _connectedObserved, 1);
                            _timerTask = StartFlushTimer(writer, pumpCt);
                        }
                    }

                    // Natural end (server closed the stream): inject Shutdown so the
                    // OnCheckpoint(Shutdown) hook still fires before iteration ends.
                    await InjectCheckpoint(CheckpointTrigger.Shutdown, "subscription ended").ConfigureAwait(false);
                }
                catch (Exception) when (pumpCt.IsCancellationRequested) {
                    // Cancellation may surface as OperationCanceledException OR as
                    // RpcException(Cancelled) depending on where the gRPC call gets cut.
                    // Either way, the ct registration has injected the Shutdown marker —
                    // let it drain. Don't propagate as a channel error.
                }
                catch (Exception ex) {
                    writer.TryComplete(ex);
                }
            }, pumpCt);
        }

        Task StartFlushTimer(ChannelWriter<SubscribeMessage> writer, CancellationToken timerCt) {
            if (!_options.Checkpoint.AutoCheckpoint)
                return Task.CompletedTask;

            return Task.Run(async () => {
                using var timer = new PeriodicTimer(_options.Checkpoint.Interval);

                try {
                    while (await timer.WaitForNextTickAsync(timerCt).ConfigureAwait(false)) {
                        await InjectCheckpoint(CheckpointTrigger.Interval).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) {
                    // Expected on shutdown
                }
            }, timerCt);
        }
    }

    #endregion

    #region ->> Remote (server) <<-

    public async ValueTask CompleteCheckpoint(
        Checkpoint         checkpoint,
        CancellationToken  ct = default
    ) {
        if (checkpoint.Positions.Length == 0)
            return;

        await _checkpointStore
            .StoreAsync(_options.DroneId, checkpoint.CheckpointId, checkpoint.Positions, ct)
            .ConfigureAwait(false);

        _tracker.FlushUpTo(checkpoint.Positions);

        // When bidirectional gRPC lands, branch on checkpoint.Trigger == Shutdown here
        // to send an outbound Disconnect message in addition to the receipt. For the
        // single-stream impl, the receipt is the same shape regardless of trigger.
    }

    #endregion

    #region ->> Local (drone state) <<-

    public void Track(Record record)           => _tracker.Track(record);

    public void Track(RecordPosition position) => _tracker.Track(position);

    public void Track(IEnumerable<Record> records) {
        // Hold the write lock for the whole batch so InjectCheckpoint's snapshot
        // observes either pre-batch or post-batch state — never partial.
        lock (_writeLock) {
            _tracker.Track(records);
        }
    }

    public ValueTask InjectCheckpoint(CheckpointTrigger trigger, string? reason = null) {
        // The lock serializes all writers so the Shutdown TryWrite + TryComplete pair
        // is observably atomic — no Interval/Manual marker can sneak in between.
        // Once _shutdownInjected flips, every subsequent caller (including a second
        // Shutdown) is dropped at the door.
        //
        // Positions are intentionally empty here: they get stamped at DRAIN time by
        // the Subscribe reader. By the time the marker is yielded, any in-flight
        // ProcessRecord / ProcessBatch + auto-track has completed, so the snapshot
        // reflects the tracker's actual post-dispatch state.
        lock (_writeLock) {
            if (_shutdownInjected) return ValueTask.CompletedTask;

            var now = _clock.Now();

            var marker = new Checkpoint(
                now,
                trigger,
                now.DateTimeOffset,
                []
            ) {
                Reason = reason
            };

            _channelWriter.TryWrite(marker);

            if (trigger == CheckpointTrigger.Shutdown) {
                _shutdownInjected = true;
                _channelWriter.TryComplete();
            }
        }

        return ValueTask.CompletedTask;
    }

    #endregion

    #region ->> Internals <<-

    RecordPosition[] SnapshotPositions() =>
        _tracker.Snapshot()
            .Where(x => x.LogPosition != LogPosition.Earliest)
            .ToArray();

    RecordPosition[] SnapshotPositionsLocked() {
        // Held under _writeLock so concurrent Track / TrackBatch calls observe a
        // consistent state — snapshot either captures pre-track or post-track state,
        // never partial.
        lock (_writeLock) {
            return SnapshotPositions();
        }
    }

    #endregion

    public async ValueTask DisposeAsync() {
        // Cancel the source CTS first so pump and timer wake up and exit. Awaiting
        // _pumpTask gives us a happens-before barrier for any field assignments the
        // pump made (notably _timerTask), so the subsequent _timerTask read is safe.
        if (_cts is not null) {
            try { await _cts.CancelAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        if (_pumpTask is not null) {
            try { await _pumpTask.ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        if (_timerTask is not null) {
            try { await _timerTask.ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        _cts?.Dispose();
        _tracker.Dispose();
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
