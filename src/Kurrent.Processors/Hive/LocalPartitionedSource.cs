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
/// catch-up filter, and writes the drone's checkpoints under its own
/// <see cref="ProcessorOptions.DroneId"/>.
/// </summary>
/// <remarks>
/// Owns the drone's <see cref="PartitionedPositionTracker"/>. The master holds the gRPC
/// subscription and the flush timer; local is the pipe.
/// </remarks>
sealed class LocalPartitionedSource : ISubscriptionSource {
    readonly MasterSubscriptionSource _master;
    readonly string                   _droneId;
    readonly Partition[]              _assignedPartitions;
    readonly ProcessorOptions         _settings;
    readonly CheckpointStore          _checkpointStore;
    readonly PartitionedPositionTracker _tracker = new();
    readonly Channel<SubscribeMessage> _inbox;
    readonly HybridLogicalClock       _clock;

    int _shutdownInjected;

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

        _myStart = new Dictionary<Partition, LogPosition>(startPositions.Length);

        foreach (var p in startPositions)
            _myStart[p.Partition] = p.LogPosition;

        _tracker.Reset(startPositions);

        // Cancellation triggers the shutdown barrier on this drone's local inbox.
        using var ctRegistration = ct.UnsafeRegister(static state => {
            var src = (LocalPartitionedSource)state!;
            _ = src.InjectCheckpoint(CheckpointTrigger.Shutdown, "subscription canceled");
        }, this);

        // Reader uses None — drains until the inbox is completed by InjectCheckpoint(Shutdown).
        await foreach (var msg in _inbox.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
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

                default:
                    yield return msg;
                    break;
            }
        }
    }

    #region ->> Remote (server) <<-

    public async ValueTask CompleteCheckpoint(
        Checkpoint         checkpoint,
        CancellationToken  ct = default
    ) {
        if (checkpoint.Positions.Length == 0)
            return;

        await _checkpointStore
            .StoreAsync(_droneId, checkpoint.CheckpointId, checkpoint.Positions, ct)
            .ConfigureAwait(false);

        _tracker.FlushUpTo(checkpoint.Positions);

        // Hive routing for Shutdown markers is deferred to a follow-up. For now,
        // persist locally regardless of trigger.
    }

    #endregion

    #region ->> Local (drone state) <<-

    public void Track(Record record)           => _tracker.Track(record);

    public void Track(RecordPosition position) => _tracker.Track(position);

    public void Track(IEnumerable<Record> records) => _tracker.Track(records);

    public ValueTask InjectCheckpoint(CheckpointTrigger trigger, string? reason = null) {
        if (trigger == CheckpointTrigger.Shutdown
            && Interlocked.Exchange(ref _shutdownInjected, 1) == 1)
            return ValueTask.CompletedTask;

        var now = _clock.Now();

        var marker = new Checkpoint(
            now,
            trigger,
            now.DateTimeOffset,
            SnapshotPositions()
        ) {
            Reason = reason
        };

        _inbox.Writer.TryWrite(marker);

        if (trigger == CheckpointTrigger.Shutdown)
            _inbox.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }

    #endregion

    public ValueTask DisposeAsync() {
        _tracker.Dispose();
        return ValueTask.CompletedTask;
    }

    #region ->> Internals <<-

    RecordPosition[] SnapshotPositions() =>
        _tracker.Snapshot()
            .Where(x => x.LogPosition != LogPosition.Earliest)
            .ToArray();

    #endregion
}
