#pragma warning disable CS8509
#pragma warning disable CS8524

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors.Gateway;


sealed class SimpleGateway : ClientModuleBase, IProcessorGateway {
    readonly StreamsClient _streams;

    ProcessorSettings                _settings    = null!;
    Channel<ProcessorControlMessage> _channel     = null!;
    CancellationTokenSource          _cancellator = null!;

    StreamName CheckpointStreamName() =>
        $"$processors/{_settings.ProcessorId}/checkpoints";

    internal SimpleGateway(KurrentClient client) : base(client) {
        _streams = new StreamsClient(client);
    }

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

            if (record.Value is not Contracts.PartitionsCheckpointed data || data.Positions.Length == 0)
                return RecordPosition.Unset;

            var pos = data.Positions[0];
            return RecordPosition.From(pos.LogPosition, pos.Stream!, pos.StreamRevision!);
        }
        catch (StreamNotFoundException) {
            return RecordPosition.Unset; // or Earliest?
        }
    }

    async ValueTask WriteCheckpointAsync(RecordPosition position, CancellationToken ct) {
        var evt = new Contracts.PartitionsCheckpointed {
            ProcessorId  = _settings.ProcessorId,
            WorkerId     = _settings.WorkerId,
            CheckpointId = string.Empty,
            Timestamp    = Time.GetUtcNow(),
            Positions    = [
                new Contracts.PartitionPosition {
                    Partition      = 0,
                    LogPosition    = position.LogPosition,
                    Stream         = position.Stream,
                    StreamRevision = (int?)position.Revision
                }
            ]
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
        async ValueTask PumpAsync(RecordPosition startPosition, CancellationToken ct) {
            var settings = _settings;

            try {
                var subscription = _streams.Subscribe(
                    SerializerProvider, MetadataDecoder,
                    settings.Subscription.Filter, startPosition,
                    settings.Worker.SkipDecoding, ct);

                await foreach (var msg in subscription.ConfigureAwait(false)) {
                    if (msg.IsConnected) {
                        await _channel.Writer
                            .WriteAsync(new WorkOrder(msg.AsConnected.SessionId, [startPosition]), ct)
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
