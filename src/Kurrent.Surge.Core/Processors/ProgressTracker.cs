using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming;

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
    /// Seeds each owned partition's committed position with the resume point the checkpointer
    /// resolved at startup — loaded from the store, or the configured initial position.
    /// </summary>
    /// <remarks>
    /// Without this the committed side reads <c>Unset</c> until the first checkpoint, and
    /// <see cref="DroneProgress.UncommittedBytes"/> — a distance from the resume point — has nothing to
    /// measure against and reports zero while an entire catch-up sits at risk. A start of
    /// <see cref="LogPosition.Latest"/> is skipped: it is a sentinel, not a position, and the real edge
    /// is only known once the subscription lands on it.
    /// </remarks>
    public void Initialize(RecordPosition[] startPositions) {
        foreach (var position in startPositions) {
            if (position.LogPosition == LogPosition.Latest || position.LogPosition == LogPosition.Unset)
                continue;

            ref var slot = ref _partitions[position.Partition.Value];
            slot = slot with { LastCommittedPosition = position };
        }
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
    public void TrackCheckpoint(RecordPosition[] positions) {
        _totalCheckpoints++;
        _uncommittedRecords = 0;

        foreach (var position in positions) {
            ref var slot = ref _partitions[position.Partition.Value];

            var before = slot.UncommittedBytes;
            slot = slot with { LastCommittedPosition = position };
            _uncommittedBytes += slot.UncommittedBytes - before;
        }
    }

    // Reads — field returns, nothing computed.
    public long TotalRecords       => _totalRecords;
    public long TotalCheckpoints   => _totalCheckpoints;
    public long UncommittedRecords => _uncommittedRecords;
    public long UncommittedBytes   => _uncommittedBytes;

    /// <summary>
    /// On-demand snapshot. Counters are copied off the fields; rate and elapsed are the only computed
    /// values (they depend on "now"), and the partition slots reduce to the committed watermarks.
    /// Every member is a value type, so nothing allocates.
    /// </summary>
    public DroneProgress Progress {
        get {
            var elapsed      = _timeProvider.GetElapsedTime(_startTimestamp);
            var totalSeconds = elapsed.TotalSeconds;

            var (lowest, highest) = CommittedWatermarks();

            return new DroneProgress {
                TotalRecords     = _totalRecords,
                RecordsPerSecond = totalSeconds > 0 ? _totalRecords / totalSeconds : 0,
                StartedAt        = _startedAt,
                Elapsed          = elapsed,
                LastProcessedAt  = _lastProcessedAt,

                LowestCommittedPosition  = lowest,
                HighestCommittedPosition = highest,

                UncommittedRecords = _uncommittedRecords,
                UncommittedBytes   = _uncommittedBytes
            };
        }
    }

    // Reduce the owned slots to the two aggregates that mean something outside this class: the lowest
    // committed position (a restart resumes from the least-advanced partition) and the highest (their
    // distance is commit skew). Partitions that have never committed are skipped — they hold Unset and
    // would otherwise drag the lowest to zero. A loop over a fixed array of structs; no allocation.
    (RecordPosition Lowest, RecordPosition Highest) CommittedWatermarks() {
        var lowest  = RecordPosition.Unset;
        var highest = RecordPosition.Unset;

        foreach (var partition in _partitions) {
            var committed = partition.LastCommittedPosition;

            if (committed.LogPosition == LogPosition.Unset)
                continue;

            if (lowest.LogPosition == LogPosition.Unset || committed.LogPosition < lowest.LogPosition)
                lowest = committed;

            if (highest.LogPosition == LogPosition.Unset || committed.LogPosition > highest.LogPosition)
                highest = committed;
        }

        return (lowest, highest);
    }

}

/// <summary>
/// One partition's position state inside <see cref="ProgressTracker"/>. Internal: callers see the
/// committed watermarks on <see cref="DroneProgress"/>, not the per-partition slots.
/// </summary>
readonly record struct PartitionProgress(
    Partition      Partition,
    RecordPosition LastTrackedPosition,
    RecordPosition LastCommittedPosition
) {
    /// <summary>
    /// Bytes tracked but not yet committed for this partition — the gap between the last tracked and
    /// last committed log position (log positions are byte offsets). Zero when nothing is pending or
    /// nothing has committed yet.
    /// </summary>
    public long UncommittedBytes {
        get {
            var tracked   = LastTrackedPosition.LogPosition;
            var committed = LastCommittedPosition.LogPosition;

            return tracked != LogPosition.Unset && committed != LogPosition.Unset && tracked > committed
                ? tracked.Value - committed.Value
                : 0;
        }
    }
}
