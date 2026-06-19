using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming;

/// <summary>
/// Tracks processing progress for a session. Push-based: the Drone feeds it explicitly at each step.
/// </summary>
/// <remarks>
/// Every value is maintained as state at the moment it changes — counting a record, tracking a
/// position, committing a checkpoint. Reads are field returns. The only thing computed at read time is
/// throughput rate / elapsed, which depend on the current clock and so can't be stored. Per-partition
/// positions live in one array sized once to the owned partition count and indexed directly by partition
/// number (owned partitions are <c>0..N-1</c>), mutated in place. Zero per-record allocation.
/// </remarks>
sealed class ProgressTracker {
    readonly TimeProvider   _timeProvider;
    readonly DateTimeOffset _startedAt;
    readonly long           _startTimestamp;

    long _totalRecords;
    long _totalCheckpoints;
    long _uncommittedRecords;   // ++ per record, reset to 0 on checkpoint
    long _uncommittedBytes;     // adjusted by each partition's gap delta on track/flush

    // The event-time of the last dispatched record. Compared against the readable head's timestamp
    // to compute source_lag_seconds (how far behind in event-time the drone is).
    DateTime? _lastProcessedAt;

    CheckpointTrigger _lastCheckpointTrigger = CheckpointTrigger.Unspecified;
    DateTimeOffset?   _lastCheckpointAt;

    // One slot per owned partition, index == partition number. Allocated once; mutated in place.
    readonly PartitionProgress[] _partitions;

    public ProgressTracker(TimeProvider timeProvider, int partitionCount) {
        _timeProvider   = timeProvider;
        _startedAt      = timeProvider.GetUtcNow();
        _startTimestamp = timeProvider.GetTimestamp();

        _partitions = new PartitionProgress[partitionCount];
        for (var i = 0; i < partitionCount; i++)
            _partitions[i] = new(Partition.From(i), RecordPosition.Unset, RecordPosition.Unset);
    }

    /// <summary>
    /// Counts a dispatched record toward throughput and advances the source-lag clock. Always
    /// called, independent of tracking, so throughput reflects every dispatched record even when
    /// AutoTrack is off.
    /// </summary>
    public void CountThroughput(Record record) {
        _totalRecords++;
        _uncommittedRecords++;
        _lastProcessedAt = record.Timestamp;
    }

    /// <summary>
    /// Counts a whole batch toward throughput in one step — the batch path resolves a single committed
    /// frontier instead of looping per record, so throughput advances by the dispatched count (the
    /// "batch trap": every delivered record counts, even with AutoTrack off) without an O(N) loop.
    /// </summary>
    /// <param name="count">Number of dispatched records in the batch.</param>
    /// <param name="lastProcessedAt">Event-time of the last record in the batch, for source-lag.</param>
    public void CountThroughput(int count, DateTime lastProcessedAt) {
        _totalRecords      += count;
        _uncommittedRecords += count;
        _lastProcessedAt    = lastProcessedAt;
    }

    /// <summary>
    /// Records that a position has been tracked (handled, pending commit). The position carries its own
    /// partition, so it indexes the slot directly. Folds the partition's byte-gap delta into the running
    /// uncommitted total — no read-time loop.
    /// </summary>
    public void TrackPosition(RecordPosition position) {
        ref var slot = ref _partitions[position.Partition.Value];

        var before = slot.UncommittedBytes;                  // this partition's gap, pre-update
        slot = slot with { LastTrackedPosition = position };
        _uncommittedBytes += slot.UncommittedBytes - before; // apply the delta
    }

    /// <summary>
    /// Tracks a checkpoint completion with committed positions. Resets the uncommitted-record count and
    /// folds each partition's shrinking byte-gap into the running uncommitted total.
    /// </summary>
    public void TrackCheckpoint(Checkpoint checkpoint, RecordPosition[] positions) {
        _totalCheckpoints++;
        _uncommittedRecords    = 0;
        _lastCheckpointTrigger = checkpoint.TriggerKind;
        _lastCheckpointAt      = checkpoint.Timestamp;

        foreach (var position in positions) {
            ref var slot = ref _partitions[position.Partition.Value];

            var before = slot.UncommittedBytes;
            slot = slot with { LastFlushedPosition = position };
            _uncommittedBytes += slot.UncommittedBytes - before;
        }
    }

    // Reads — field returns, nothing computed.
    public long TotalRecords       => _totalRecords;
    public long TotalCheckpoints   => _totalCheckpoints;
    public long UncommittedRecords => _uncommittedRecords;
    public long UncommittedBytes   => _uncommittedBytes;

    /// <summary>
    /// On-demand snapshot. Counters are copied off the fields; rate / elapsed are the only computed
    /// values (they depend on "now"). The partition list is the only allocation, built here on demand.
    /// </summary>
    public DroneProgress Progress {
        get {
            var elapsed = _timeProvider.GetElapsedTime(_startTimestamp);

            var totalSeconds = elapsed.TotalSeconds;

            var throughput = new ThroughputMetrics(
                TotalRecords:     _totalRecords,
                RecordsPerSecond: totalSeconds > 0 ? _totalRecords / totalSeconds : 0,
                StartedAt:        _startedAt,
                Elapsed:          elapsed,
                LastProcessedAt:  _lastProcessedAt
            );

            var checkpoint = new CheckpointProgress(
                TotalCheckpoints:           _totalCheckpoints,
                RecordsSinceLastCheckpoint: _uncommittedRecords,
                LastTrigger:                _lastCheckpointTrigger,
                LastCheckpointAt:           _lastCheckpointAt
            );

            return new DroneProgress(throughput, checkpoint, SnapshotPartitions());
        }
    }

    // A frozen copy of every owned partition. The drone owns its partitions from the start, so the
    // snapshot always carries all of them — idle ones simply hold Unset positions. No active filter,
    // no scan: one copy of the live slots, built only when a snapshot is requested.
    IReadOnlyList<PartitionProgress> SnapshotPartitions() => [.._partitions];
}
