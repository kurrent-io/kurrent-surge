using System.Collections.ObjectModel;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming;

/// <summary>
/// Tracks processing progress and computes metrics for a processing session.
/// Push-based: the Drone feeds it explicitly at each step, no reference to the source.
/// </summary>
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

    string            _lastCheckpointId      = "";
    CheckpointTrigger _lastCheckpointTrigger = CheckpointTrigger.Unspecified;
    DateTimeOffset?   _lastCheckpointAt;

    readonly Dictionary<Partition, RecordPosition> _trackedPositions = new();
    readonly Dictionary<Partition, RecordPosition> _flushedPositions = new();

    public ProgressTracker(TimeProvider timeProvider) {
        _timeProvider   = timeProvider;
        _startedAt      = timeProvider.GetUtcNow();
        _startTimestamp = timeProvider.GetTimestamp();
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
    /// Records that a position has been tracked (handled, pending commit) for its partition.
    /// </summary>
    public void TrackPosition(RecordPosition position, Partition partition) {
        _trackedPositions[partition] = position;
    }

    /// <summary>
    /// Tracks a checkpoint completion with committed positions.
    /// </summary>
    public void TrackCheckpoint(Checkpoint checkpoint, RecordPosition[] positions) {
        _totalCheckpoints++;
        _recordsAtLastCheckpoint = _totalRecords;
        _lastCheckpointId        = checkpoint.Id;
        _lastCheckpointTrigger   = checkpoint.TriggerKind;
        _lastCheckpointAt        = checkpoint.Timestamp;

        foreach (var position in positions)
            _flushedPositions[position.Partition] = position;
    }

    /// <summary>
    /// Computes current progress snapshot.
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

            var partitions = BuildPartitionProgress();

            return new DroneProgress(throughput, checkpoint, partitions);
        }
    }

    IReadOnlyDictionary<Partition, PartitionProgress> BuildPartitionProgress() {
        if (_flushedPositions.Count == 0 && _trackedPositions.Count == 0)
            return ReadOnlyDictionary<Partition, PartitionProgress>.Empty;

        // Collect all known partitions, then merge tracked + flushed into one entry each. A plain
        // Dictionary snapshot is cheaper to build per access than an ImmutableDictionary (AVL tree);
        // it is never mutated after return, so it is a safe read-only snapshot.
        var allPartitions = new HashSet<Partition>(_flushedPositions.Keys);
        foreach (var key in _trackedPositions.Keys)
            allPartitions.Add(key);

        var result = new Dictionary<Partition, PartitionProgress>(allPartitions.Count);

        foreach (var partition in allPartitions) {
            var lastTracked = _trackedPositions.GetValueOrDefault(partition, RecordPosition.Unset);
            var lastFlushed = _flushedPositions.GetValueOrDefault(partition, RecordPosition.Unset);
            result[partition] = new PartitionProgress(lastTracked, lastFlushed);
        }

        return result;
    }
}
