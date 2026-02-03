using System.Collections.Immutable;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;

namespace Kurrent.Processors;

/// <summary>
/// Tracks processing progress and computes metrics for a processing session.
/// </summary>
sealed class ProgressTracker {
    readonly TimeProvider   _timeProvider;
    readonly DateTimeOffset _startedAt;
    readonly long           _startTimestamp;

    long _totalRecords;
    long _totalBatches;
    long _totalCheckpoints;
    long _recordsAtLastCheckpoint;

    BarrierId         _lastCheckpointId      = BarrierId.None;
    CheckpointTrigger _lastCheckpointTrigger = CheckpointTrigger.Unspecified;
    DateTimeOffset?   _lastCheckpointAt;

    PartitionedPositionTracker? _partitionTracker;

    public ProgressTracker(TimeProvider timeProvider) {
        _timeProvider   = timeProvider;
        _startedAt      = timeProvider.GetUtcNow();
        _startTimestamp = timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Sets the partition tracker to use for position progress.
    /// </summary>
    /// <param name="tracker">The partition tracker containing position state.</param>
    public void SetPartitionTracker(PartitionedPositionTracker tracker) =>
        _partitionTracker = tracker;

    /// <summary>
    /// Tracks a batch of records being processed.
    /// </summary>
    /// <param name="recordCount">Number of records in the batch.</param>
    public void TrackRecords(int recordCount) {
        _totalRecords += recordCount;
        _totalBatches++;
    }

    /// <summary>
    /// Tracks a checkpoint completion.
    /// </summary>
    /// <param name="barrier">The checkpoint barrier that completed.</param>
    public void TrackCheckpoint(CheckpointBarrier barrier) {
        _totalCheckpoints++;
        _recordsAtLastCheckpoint  = _totalRecords;
        _lastCheckpointId  = barrier.BarrierId;
        _lastCheckpointTrigger    = barrier.Trigger;
        _lastCheckpointAt         = barrier.Timestamp;
    }

    /// <summary>
    /// Computes current progress snapshot.
    /// </summary>
    public WorkerProgress Progress {
        get {
            var elapsed = _timeProvider.GetElapsedTime(_startTimestamp);

            var throughput = new ThroughputMetrics(
                TotalRecords:     _totalRecords,
                TotalBatches:     _totalBatches,
                RecordsPerSecond: elapsed.TotalSeconds > 0 ? _totalRecords / elapsed.TotalSeconds : 0,
                AverageBatchSize: _totalBatches > 0 ? (double)_totalRecords / _totalBatches : 0,
                StartedAt:        _startedAt,
                Elapsed:          elapsed
            );

            var checkpoint = new CheckpointProgress(
                TotalCheckpoints:           _totalCheckpoints,
                RecordsSinceLastCheckpoint: _totalRecords - _recordsAtLastCheckpoint,
                //LastBarrierId:              _lastCheckpointId,
                LastTrigger:                _lastCheckpointTrigger,
                LastCheckpointAt:            _lastCheckpointAt
            );

            var partitions = BuildPartitionProgress();

            return new WorkerProgress(throughput, checkpoint, partitions);
        }
    }

    ImmutableDictionary<Partition, PartitionProgress> BuildPartitionProgress() {
        if (_partitionTracker is null)
            return ImmutableDictionary<Partition, PartitionProgress>.Empty;

        var tracked = _partitionTracker.LastTrackedPositions;
        var flushed = _partitionTracker.LastFlushedPositions;

        var builder = ImmutableDictionary.CreateBuilder<Partition, PartitionProgress>();

        foreach (var (partition, lastFlushed) in flushed) {
            var lastTracked = tracked.GetValueOrDefault(partition, LogPosition.Unset);
            builder.Add(partition, new PartitionProgress(lastTracked, lastFlushed));
        }

        return builder.ToImmutable();
    }
}
