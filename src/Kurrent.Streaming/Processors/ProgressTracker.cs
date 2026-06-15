using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming;

/// <summary>
/// Tracks processing progress and computes metrics for a processing session.
/// Push-based: the Drone feeds it explicitly at each step, no reference to the source.
/// </summary>
/// <remarks>
/// The hot path allocates nothing. Counters are plain fields; per-partition positions live in one array
/// sized once to the owned partition count and indexed directly by partition number (owned partitions are
/// <c>0..N-1</c>), mutated in place. A <see cref="DroneProgress"/> snapshot is built only on demand
/// (<see cref="Progress"/>) — never per record.
/// </remarks>
sealed class ProgressTracker {
    readonly TimeProvider   _timeProvider;
    readonly DateTimeOffset _startedAt;
    readonly long           _startTimestamp;

    long _totalRecords;
    long _totalCheckpoints;
    long _recordsAtLastCheckpoint;

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
            _partitions[i] = new PartitionProgress(Partition.From(i), RecordPosition.Unset, RecordPosition.Unset);
    }

    /// <summary>
    /// Counts a dispatched record toward throughput and advances the source-lag clock. Always
    /// called, independent of tracking, so throughput reflects every dispatched record even when
    /// AutoTrack is off.
    /// </summary>
    public void CountThroughput(Record record) {
        _totalRecords++;
        _lastProcessedAt = record.Timestamp;
    }

    /// <summary>
    /// Records that a position has been tracked (handled, pending commit). The position carries its own
    /// partition, so it indexes the slot directly — no lookup, no allocation.
    /// </summary>
    public void TrackPosition(RecordPosition position) {
        ref var slot = ref _partitions[position.Partition.Value];
        slot = slot with { LastTrackedPosition = position };
    }

    /// <summary>
    /// Tracks a checkpoint completion with committed positions.
    /// </summary>
    public void TrackCheckpoint(Checkpoint checkpoint, RecordPosition[] positions) {
        _totalCheckpoints++;
        _recordsAtLastCheckpoint = _totalRecords;
        _lastCheckpointTrigger   = checkpoint.TriggerKind;
        _lastCheckpointAt        = checkpoint.Timestamp;

        foreach (var position in positions) {
            ref var slot = ref _partitions[position.Partition.Value];
            slot = slot with { LastFlushedPosition = position };
        }
    }

    /// <summary>Total records dispatched so far. Cheap scalar — never allocates.</summary>
    public long TotalRecords => _totalRecords;

    /// <summary>
    /// Records tracked but not yet committed — the replay window in records. Cheap scalar; the same value
    /// the snapshot exposes as <c>CheckpointProgress.RecordsSinceLastCheckpoint</c>, without building one.
    /// </summary>
    public long UncommittedRecords => _totalRecords - _recordsAtLastCheckpoint;

    /// <summary>
    /// Bytes tracked but not yet committed across all partitions — the replay window in bytes. Sums the
    /// per-partition gap straight off the array, so the per-record hot path never builds a snapshot.
    /// </summary>
    public long UncommittedBytes {
        get {
            var total = 0L;
            foreach (var partition in _partitions)
                total += partition.UncommittedBytes;   // 0 for untouched slots
            return total;
        }
    }

    /// <summary>
    /// Builds the current progress snapshot. Counters and timings are copied off the fields; the partition
    /// list is the only allocation, and it happens here on demand — never on the per-record path.
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
                RecordsSinceLastCheckpoint: _totalRecords - _recordsAtLastCheckpoint,
                LastTrigger:                _lastCheckpointTrigger,
                LastCheckpointAt:           _lastCheckpointAt
            );

            return new DroneProgress(throughput, checkpoint, SnapshotPartitions());
        }
    }

    // Freeze a copy of the slots that have any progress. Two passes to size the result array exactly —
    // built only when a snapshot is requested, so allocation here is off the hot path.
    IReadOnlyList<PartitionProgress> SnapshotPartitions() {
        var active = 0;
        foreach (var p in _partitions)
            if (HasProgress(p))
                active++;

        if (active == 0)
            return [];

        var result = new PartitionProgress[active];

        var i = 0;
        foreach (var p in _partitions)
            if (HasProgress(p))
                result[i++] = p;

        return result;

        static bool HasProgress(PartitionProgress p) =>
            p.LastTrackedPosition.LogPosition != LogPosition.Unset ||
            p.LastFlushedPosition.LogPosition != LogPosition.Unset;
    }
}
