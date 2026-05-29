using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Polly;

namespace Kurrent.Streaming.Hive;

/// <summary>
/// The one gRPC subscription shared by every drone in a <see cref="Hive"/>.
/// Loads checkpoints, fans out <see cref="Connected"/> and server <see cref="Checkpoint"/>s to
/// every drone, routes <see cref="Record"/>s to their owner, and drives the periodic checkpoint
/// barrier timer.
/// </summary>
sealed class MasterSubscriptionSource : ClientModuleBase, IAsyncDisposable {
    readonly StreamsClient      _streams;
    readonly ProcessorOptions   _settings;
    readonly HybridLogicalClock _clock;
    readonly ICheckpointStore    _checkpointStore;
    readonly ResiliencePipeline _pipeline;

    readonly Dictionary<string, DroneRegistration>                 _drones = new();
    readonly Dictionary<Partition, ChannelWriter<SubscribeMessage>> _routes = new();
    readonly Dictionary<string, RecordPosition[]>               _startPositionsByDrone = new();

    DroneRegistration[] _dronesSnapshot = [];
    RecordPosition      _lowWatermark   = RecordPosition.Earliest;

    public MasterSubscriptionSource(
        KurrentClient client,
        ProcessorOptions settings,
        ICheckpointStore checkpointStore,
        TimeProvider timeProvider
    ) : base(client) {
        _streams         = new StreamsClient(client);
        _settings        = settings;
        _clock           = new HybridLogicalClock(timeProvider);
        _checkpointStore = checkpointStore;
        _pipeline        = ResilientSubscription.BuildPipeline(settings.Resilience, Logger);
    }

    public void RegisterDrone(string droneId, Partition[] partitions, ChannelWriter<SubscribeMessage> writer) {
        _drones[droneId] = new DroneRegistration(droneId, partitions, writer);
        foreach (var p in partitions)
            _routes[p] = writer;
    }

    public RecordPosition[] GetStartPositions(string droneId) =>
        _startPositionsByDrone.TryGetValue(droneId, out var positions) ? positions : [];

    public async ValueTask InitializeAsync(CancellationToken ct) {
        // Single stream read — every drone resolves its slice from the same checkpoint.
        var checkpoint = await _checkpointStore.LoadLatestAsync("master", ct).ConfigureAwait(false);

        var existing = checkpoint.Positions.ToDictionary(p => p.Partition);
        var defaultPos = _settings.Subscription.InitialPosition == SubscriptionInitialPosition.Earliest
            ? RecordPosition.Earliest
            : RecordPosition.Latest;

        foreach (var reg in _drones.Values) {
            var resolved = new RecordPosition[reg.Partitions.Length];
            for (var i = 0; i < reg.Partitions.Length; i++) {
                var p = reg.Partitions[i];
                resolved[i] = existing.TryGetValue(p, out var found)
                    ? found
                    : defaultPos with { Partition = p };
            }
            _startPositionsByDrone[reg.DroneId] = resolved;
        }

        _lowWatermark = checkpoint.Positions.Length > 0 ? checkpoint.LowWatermark : defaultPos;

        _dronesSnapshot = _drones.Values.ToArray();
    }

    public async Task RunAsync(CancellationToken ct) {
        Exception?               fault    = null;
        CancellationTokenSource? timerCts = null;

        try {
            await ResilientSubscription
                .PumpAsync(_streams, _pipeline, _settings.Subscription.Filter, _lowWatermark, _settings.SkipDecoding, Handle, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            fault = ex;
        }
        finally {
            timerCts?.Cancel();
            timerCts?.Dispose();
            CompleteAll(fault);
        }

        return;

        // Routes each message to its drone(s). Invoked once per message by the resilient pump,
        // which reconnects transparently on transient errors. Connected fires again on every
        // reconnect, so the barrier timer starts once (keyed off timerCts, not a per-call flag)
        // and is torn down only when the pump finally stops.
        async ValueTask Handle(SubscribeMessage msg, CancellationToken token) {
            switch (msg.Value) {
                case Connected:
                    await FanOutAsync(msg, token).ConfigureAwait(false);
                    if (timerCts is null) {
                        timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        StartFlushTimer(timerCts.Token);
                    }
                    break;

                case Checkpoint:
                    await FanOutAsync(msg, token).ConfigureAwait(false);
                    break;

                case Record record:
                    var partition = Partition.From(
                        PartitionCalculator.XXHash.Calculate(record.Stream, _settings.MaxParallelism));

                    var tagged = record with {
                        Position = record.Position with { Partition = partition }
                    };

                    if (_routes.TryGetValue(partition, out var writer))
                        await writer.WriteAsync(tagged, token).ConfigureAwait(false);
                    break;
            }
        }
    }

    #region ->> Internals <<-

    void StartFlushTimer(CancellationToken ct) {
        _ = Task.Run(async () => {
            using var timer = new PeriodicTimer(_settings.Checkpoint.Interval);
            try {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) {
                    var now = _clock.Now();
                    Checkpoint barrier = new IntervalCheckpoint(
                        new CheckpointId(now),
                        now.DateTimeOffset);

                    await FanOutAsync(barrier, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    async ValueTask FanOutAsync(SubscribeMessage msg, CancellationToken ct) {
        foreach (var reg in _dronesSnapshot)
            await reg.Writer.WriteAsync(msg, ct).ConfigureAwait(false);
    }

    void CompleteAll(Exception? fault) {
        foreach (var reg in _dronesSnapshot) {
            if (fault is not null) reg.Writer.TryComplete(fault);
            else                   reg.Writer.TryComplete();
        }
    }

    #endregion

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    sealed record DroneRegistration(
        string DroneId,
        Partition[] Partitions,
        ChannelWriter<SubscribeMessage> Writer
    );
}
