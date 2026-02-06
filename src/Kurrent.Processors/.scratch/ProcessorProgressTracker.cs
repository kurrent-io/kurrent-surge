// using System.Collections.Immutable;
// using Kurrent.Client.Processors.Checkpointing;
// using Kurrent.Client.Processors.Gateway.Messages;
//
// namespace Kurrent.Client.Streams;
//
// /// <summary>
// /// Unified tracker for processing progress, combining position tracking with throughput metrics.
// /// </summary>
// sealed class ProcessorProgressTracker : IDisposable {
//     readonly TimeProvider                _timeProvider;
//     readonly PartitionedPositionTracker  _positions;
//     readonly DateTimeOffset              _startedAt;
//     readonly long                        _startTimestamp;
//
//     long _totalRecords;
//     long _totalBatches;
//     long _totalCheckpoints;
//     long _recordsAtLastCheckpoint;
//
//     LogPosition              _lastCheckpointPosition = LogPosition.Unset;
//     CheckpointBarrierTrigger _lastCheckpointTrigger  = CheckpointBarrierTrigger.Unspecified;
//     DateTimeOffset?          _lastCheckpointAt;
//
//     public ProcessorProgressTracker(TimeProvider timeProvider)
//         : this(timeProvider, ImmutableDictionary<Partition, LogPosition>.Empty, LogPosition.Unset) { }
//
//     public ProcessorProgressTracker(
//         TimeProvider timeProvider,
//         IReadOnlyDictionary<Partition, LogPosition> positions,
//         LogPosition lastBarrierPosition
//     ) {
//         _timeProvider   = timeProvider;
//         _positions      = new PartitionedPositionTracker(positions, lastBarrierPosition);
//         _startedAt      = timeProvider.GetUtcNow();
//         _startTimestamp = timeProvider.GetTimestamp();
//     }
//
//     /// <summary>
//     /// Gets the number of partitions being tracked.
//     /// </summary>
//     public int PartitionCount => _positions.PartitionCount;
//
//     /// <summary>
//     /// Tracks positions from a batch of records and updates throughput metrics.
//     /// </summary>
//     /// <param name="batch">The batch of records to track.</param>
//     /// <returns>Total number of positions tracked across all partitions.</returns>
//     public int Track(RecordBatch batch) {
//         var tracked = _positions.Track(batch);
//         _totalRecords += batch.Count;
//         _totalBatches++;
//         return tracked;
//     }
//
//     /// <summary>
//     /// Prepares a checkpoint without committing. Call <see cref="PendingCheckpoint.Complete"/> after persistence succeeds.
//     /// </summary>
//     /// <param name="barrier">The checkpoint barrier to prepare.</param>
//     /// <returns>A pending checkpoint that can be completed after persistence.</returns>
//     public PendingCheckpoint PrepareCheckpoint(CheckpointBarrier barrier) {
//         var positions = _positions.PrepareFlush(barrier.BarrierId).Positions;
//         return new PendingCheckpoint(this, barrier, positions);
//     }
//
//     /// <summary>
//     /// Commits a checkpoint: flushes positions and updates metrics.
//     /// </summary>
//     internal void CommitCheckpoint(CheckpointBarrier barrier, ImmutableDictionary<Partition, LogPosition> positions) {
//         _positions.FlushUpTo(positions);
//
//         _totalCheckpoints++;
//         _recordsAtLastCheckpoint = _totalRecords;
//         _lastCheckpointPosition  = barrier.BarrierId;
//         _lastCheckpointTrigger   = barrier.Trigger;
//         _lastCheckpointAt        = barrier.Timestamp;
//     }
//
//     /// <summary>
//     /// Resets the position tracker with new partition positions, reusing existing trackers where possible.
//     /// Throughput metrics are preserved.
//     /// </summary>
//     /// <param name="positions">The new partition positions.</param>
//     /// <param name="lastBarrierPosition">The new last barrier position.</param>
//     public void Reset(IReadOnlyDictionary<Partition, LogPosition> positions, LogPosition lastBarrierPosition) =>
//         _positions.Reset(positions, lastBarrierPosition);
//
//     /// <summary>
//     /// Computes current progress snapshot.
//     /// </summary>
//     public ProcessorProgress Progress {
//         get {
//             var elapsed = _timeProvider.GetElapsedTime(_startTimestamp);
//
//             var throughput = new ThroughputMetrics(
//                 TotalRecords:     _totalRecords,
//                 TotalBatches:     _totalBatches,
//                 RecordsPerSecond: elapsed.TotalSeconds > 0 ? _totalRecords / elapsed.TotalSeconds : 0,
//                 AverageBatchSize: _totalBatches > 0 ? (double)_totalRecords / _totalBatches : 0,
//                 StartedAt:        _startedAt,
//                 Elapsed:          elapsed
//             );
//
//             var checkpoint = new CheckpointProgress(
//                 TotalCheckpoints:           _totalCheckpoints,
//                 RecordsSinceLastCheckpoint: _totalRecords - _recordsAtLastCheckpoint,
//                 LastPosition:               _lastCheckpointPosition,
//                 LastTrigger:                _lastCheckpointTrigger,
//                 LastCompletedAt:            _lastCheckpointAt
//             );
//
//             var partitions = BuildPartitionProgress();
//
//             return new ProcessorProgress(throughput, checkpoint, partitions);
//         }
//     }
//
//     ImmutableDictionary<Partition, PartitionProgress> BuildPartitionProgress() {
//         var tracked = _positions.LastTrackedPositions;
//         var flushed = _positions.LastFlushedPositions;
//
//         var builder = ImmutableDictionary.CreateBuilder<Partition, PartitionProgress>();
//
//         foreach (var (partition, lastFlushed) in flushed) {
//             var lastTracked = tracked.GetValueOrDefault(partition, LogPosition.Unset);
//             builder.Add(partition, new PartitionProgress(lastTracked, lastFlushed));
//         }
//
//         return builder.ToImmutable();
//     }
//
//     /// <inheritdoc />
//     public void Dispose() => _positions.Dispose();
// }
//
// /// <summary>
// /// A prepared checkpoint that can be committed after external persistence succeeds.
// /// </summary>
// [PublicAPI]
// public readonly struct PendingCheckpoint {
//     readonly ProcessorProgressTracker _tracker;
//     readonly CheckpointBarrier        _barrier;
//
//     /// <summary>
//     /// The positions to be flushed on <see cref="Complete"/>.
//     /// </summary>
//     public ImmutableDictionary<Partition, LogPosition> Positions { get; }
//
//     internal PendingCheckpoint(
//         ProcessorProgressTracker tracker,
//         CheckpointBarrier barrier,
//         ImmutableDictionary<Partition, LogPosition> positions
//     ) {
//         _tracker  = tracker;
//         _barrier  = barrier;
//         Positions = positions;
//     }
//
//     /// <summary>
//     /// Commits the checkpoint: flushes positions and updates metrics.
//     /// </summary>
//     public void Complete() => _tracker.CommitCheckpoint(_barrier, Positions);
// }
