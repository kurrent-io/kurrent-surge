#pragma warning disable CS8509
#pragma warning disable CS8524

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Exceptions;
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

using static System.StringComparison;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;

namespace Kurrent.Processors.Gateway;

public readonly record struct Connected(SessionId SessionId);

readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint> {
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

            // determine the actual stream state when not found (v1 protocol quirk)
            if (grpcSession.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                var info = await streams.GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _                      => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not Confirmation)
                throw new InvalidOperationException("Expected subscription confirmation as the first message from the server.");

            sessionId = SessionId.From(Guid.Parse(grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId));
        }
        catch (RpcException rex) {
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            // when stream is tombstoned an exception is thrown for some weird reason
            if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
                var details  = rex.Message.Split(">>").Last().Trim();
                throw new LogPositionNotFoundException(position, details);
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

                    case CaughtUp:
                        yield return resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                        break;

                    case Event:
                        var record = await ProtocolMapping.MapToRecordAsync(
                            resp.Event,
                            sequencer.FetchNext(),
                            serializerProvider, metadataDecoder,
                            SchemaRegistryPolicy.NoRequirements,
                            skipDecoding,
                            ct
                        ).ConfigureAwait(false);

                        // client-side filter until v2 protocol has a proper Subscription operation
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

sealed class SimpleGateway : ClientModuleBase, IProcessorGateway {
    readonly StreamsClient _streams;

    ProcessorSettings                _settings    = null!;
    Channel<ProcessorControlMessage> _channel     = null!;
    CancellationTokenSource          _cancellator = null!;

    StreamName CheckpointStreamName() =>
        $"$processors/{_settings.ProcessorId}/checkpoints";

    internal SimpleGateway(KurrentClient client) : base(client) =>
        _streams = new StreamsClient(client);

    public async IAsyncEnumerable<ProcessorControlMessage> ConnectAsync(
        ProcessorSettings settings,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        _settings    = settings;
        _cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _channel = Channel.CreateBounded<ProcessorControlMessage>(
            new BoundedChannelOptions(settings.Worker.BufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        var position = await ResolveStartPosition();

        StartRelay(position);

        await foreach (var msg in _channel.Reader.ReadAllAsync(_cancellator.Token).ConfigureAwait(false))
            yield return msg;

        async Task<RecordPosition> ResolveStartPosition() {
            var pos = settings.Subscription.StartPosition
                   ?? await ReadLastCheckpointAsync(_cancellator.Token).ConfigureAwait(false);

            if (pos.IsUnset) {
                pos = settings.Subscription.InitialPosition == SubscriptionInitialPosition.Earliest
                    ? RecordPosition.Earliest
                    : RecordPosition.Latest;
            }

            return pos with { Partition = Partition.From(0) };
        }
    }

    public ValueTask SendAsync(WorkerMessage message) {
        return message.SwitchAsync(
            onWorkerReady: OnWorkerReady,
            onCheckpointComplete: OnCheckpointComplete,
            onDisconnect: OnDisconnect);

        ValueTask OnWorkerReady(WorkerReady ready) {
            StartFlushTimer();
            return ValueTask.CompletedTask;
        }

        ValueTask OnCheckpointComplete(CheckpointComplete complete) =>
            WriteCheckpointAsync(complete.Positions[Partition.From(0)], _cancellator.Token);

        ValueTask OnDisconnect(Disconnect disconnect) =>
            disconnect.Positions.Count > 0
                ? WriteCheckpointAsync(disconnect.Positions[Partition.From(0)], CancellationToken.None)
                : ValueTask.CompletedTask;
    }

    #region Checkpointing

    async Task<RecordPosition> ReadLastCheckpointAsync(CancellationToken ct) {
        var stream = CheckpointStreamName();

        try {
            var record = await _streams.ReadLastStreamRecord(stream, skipDecoding: false, ct)
                .ConfigureAwait(false);

            if (record.Value is not Contracts.PartitionCheckpointed data)
                return RecordPosition.Unset;

            return RecordPosition.From(data.LogPosition, data.Stream!, data.StreamRevision!);
        }
        catch (StreamNotFoundException) {
            return RecordPosition.Unset; // or Earliest?
        }
    }

    async ValueTask WriteCheckpointAsync(RecordPosition position, CancellationToken ct) {
        var evt = new Contracts.PartitionCheckpointed {
            Partition      = 0,
            LogPosition    = position.LogPosition,
            Stream         = position.Stream,
            StreamRevision = (int?)position.Revision,
            ProcessorId    = _settings.ProcessorId,
            WorkerId       = _settings.WorkerId,
            Timestamp      = Time.GetUtcNow()
        };

        var stream = CheckpointStreamName();

        var request = AppendStreamRequest.For(stream)
            .ExpectingState(ExpectedStreamState.Any)
            .WithRecord(rec => rec.WithValue(evt))
            .Build();

        var rsp = await _streams.AppendStream(request, ct).ConfigureAwait(false);

    }

    #endregion

    void StartRelay(RecordPosition from) {
        _ = Task.Run(() => PumpAsync(from, _cancellator.Token).AsTask(), _cancellator.Token);

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        async ValueTask PumpAsync(RecordPosition from, CancellationToken ct) {
            var settings = _settings;

            try {
                var subscription = _streams.Subscribe(
                    SerializerProvider, MetadataDecoder,
                    settings.Subscription.Filter, from,
                    settings.Worker.SkipDecoding, ct);

                await foreach (var msg in subscription.ConfigureAwait(false)) {
                    if (msg.IsConnected) {
                        await _channel.Writer
                            .WriteAsync(new WorkOrder(msg.AsConnected.SessionId, [from]), ct)
                            .ConfigureAwait(false);
                    }
                    else if (msg.IsRecord) {
                        var record    = msg.AsRecord;
                        var partition = PartitionCalculator.XXHash.Calculate(record.Stream, settings.MaxParallelism);

                        var tagged = record with {
                            Position = record.Position with { Partition = Partition.From(partition) }
                        };

                        await _channel.Writer.WriteAsync(tagged, ct).ConfigureAwait(false);
                    }
                    else if (msg.IsCheckpoint) {
                        await _channel.Writer.WriteAsync(msg.AsCheckpoint, ct).ConfigureAwait(false);
                    }
                }

                _channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) {
                _channel.Writer.TryComplete();
            }
            catch (Exception ex) {
                _channel?.Writer.TryComplete(ex.AsWellKnownException());
            }
        }
    }

    void StartFlushTimer() {
        _ = Task.Run(async () => {
            var clock = new HybridLogicalClock(Time);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.Checkpoint.Interval));

            try {
                while (await timer.WaitForNextTickAsync(_cancellator.Token).ConfigureAwait(false)) {
                    var now = clock.Now();

                    var checkpoint = new Checkpoint(
                        CheckpointId.From(now),
                        CheckpointTrigger.Interval,
                        now.DateTimeOffset);

                    await _channel.Writer
                        .WriteAsync(checkpoint, _cancellator.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) {
                // Expected on shutdown
            }
        }, _cancellator.Token);
    }


    public async ValueTask DisposeAsync() {
        _channel?.Writer.TryComplete();
        if (_cancellator is not null) {
            await _cancellator.CancelAsync().ConfigureAwait(false);
            _cancellator.Dispose();
        }
    }
}
