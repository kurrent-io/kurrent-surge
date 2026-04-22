using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors.Hive;

/// <summary>
/// Per-drone pipe: receives messages from <see cref="MasterSubscriptionSource"/>, applies a
/// catch-up filter, snapshots positions on checkpoint barriers, and writes the drone's
/// checkpoints under its own <see cref="ProcessorOptions.DroneId"/>.
/// </summary>
/// <remarks>
/// Owns the drone's <see cref="PartitionedPositionTracker"/> and pending-flush state.
/// The master holds the gRPC subscription and the flush timer; local is the pipe.
/// </remarks>
sealed class LocalPartitionedSource : ISubscriptionSource {
    readonly MasterSubscriptionSource _master;
    readonly string                   _droneId;
    readonly Partition[]              _assignedPartitions;
    readonly ProcessorOptions         _settings;
    readonly CheckpointStore          _checkpointStore;
    readonly PartitionedPositionTracker _tracker = new();
    readonly Dictionary<string, PendingPositionsFlush> _pendingFlushes = new();
    readonly Channel<SubscribeMessage> _inbox;
    readonly HybridLogicalClock       _clock;

    Dictionary<Partition, LogPosition> _myStart = new();

    public LocalPartitionedSource(
        MasterSubscriptionSource master,
        string droneId,
        Partition[] assignedPartitions,
        ProcessorOptions settings,
        CheckpointStore checkpointStore,
        TimeProvider timeProvider
    ) {
        _master             = master;
        _droneId            = droneId;
        _assignedPartitions = assignedPartitions;
        _settings           = settings;
        _checkpointStore    = checkpointStore;
        _clock              = new HybridLogicalClock(timeProvider);

        _inbox = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(settings.BufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        _master.RegisterDrone(_droneId, _assignedPartitions, _inbox.Writer);
    }

    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var startPositions = _master.GetStartPositions(_droneId);

        var positionMap = new Dictionary<Partition, RecordPosition>(startPositions.Length);
        _myStart        = new Dictionary<Partition, LogPosition>(startPositions.Length);

        foreach (var p in startPositions) {
            positionMap[p.Partition] = p.Position;
            _myStart[p.Partition]    = p.Position.LogPosition;
        }

        _tracker.Reset(positionMap);

        await foreach (var msg in _inbox.Reader.ReadAllAsync(ct).ConfigureAwait(false)) {
            switch (msg.Value) {
                case Record record: {
                    // Master opened gRPC at the global low watermark, so records older than
                    // our own checkpoint for the same partition have already been processed.
                    if (_myStart.TryGetValue(record.Position.Partition, out var myStart)
                        && record.Position.LogPosition <= myStart)
                        continue;

                    yield return record;
                    break;
                }

                case Checkpoint trigger when trigger.Positions.Count == 0:
                    yield return SnapshotCheckpointMessage(trigger.Trigger);
                    break;

                default:
                    yield return msg;
                    break;
            }
        }
    }

    public void Track(Record record) => _tracker.Track(record);

    public async ValueTask<PartitionPositions> CompleteCheckpointAsync(
        string checkpointId, CancellationToken ct = default
    ) {
        if (!_pendingFlushes.Remove(checkpointId, out var pendingFlush))
            return PartitionPositions.Empty;

        var positions = FilterEarliest(pendingFlush.Positions);

        if (positions.Count == 0)
            return PartitionPositions.Empty;

        var partitionPositions = positions
            .Select(kvp => new PartitionPosition(kvp.Key, kvp.Value))
            .ToArray();

        await _checkpointStore
            .StoreAsync(_droneId, checkpointId, partitionPositions, ct)
            .ConfigureAwait(false);

        pendingFlush.Execute();

        return positions;
    }

    public Checkpoint CreateShutdownCheckpoint() =>
        SnapshotCheckpointMessage(CheckpointTrigger.Shutdown);

    public ValueTask DisposeAsync() {
        _tracker.Dispose();
        return ValueTask.CompletedTask;
    }

    #region ->> Internals <<-

    Checkpoint SnapshotCheckpointMessage(CheckpointTrigger trigger) {
        var now          = _clock.Now();
        var checkpointId = CheckpointId.From(now);
        var pendingFlush = _tracker.PrepareFlush();

        _pendingFlushes[checkpointId] = pendingFlush;

        return new Checkpoint(
            checkpointId,
            trigger,
            now.DateTimeOffset,
            FilterEarliest(pendingFlush.Positions)
        );
    }

    static PartitionPositions FilterEarliest(IReadOnlyDictionary<Partition, RecordPosition> positions) =>
        positions
            .Where(x => x.Value.LogPosition != LogPosition.Earliest)
            .ToImmutableDictionary();

    #endregion
}
