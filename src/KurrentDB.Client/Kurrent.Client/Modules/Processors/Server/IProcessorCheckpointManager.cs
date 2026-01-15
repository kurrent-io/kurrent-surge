// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Processors.Checkpointing.Server;
//
// /// <summary>
// /// Tracks inflight positions (pushed to the workers) and raises a <see cref="CheckpointBarrierTrigger"/> event when thresholds are met for checkpointing.
// /// Because it is event sourced later on we will be able to reconstruct the state of the checkpointer at any time.
// /// Need to validate agains spec later.
// /// </summary>
// public interface IProcessorCheckpointManager : IAsyncDisposable {
//     /// <summary>
//     /// The unique identifier for the processor.
//     /// </summary>
//     string ProcessorId { get; }
//
//     /// <summary>
//     /// The current state of the processor checkpointer.
//     /// </summary>
//     ProcessorCheckpointManagerState State { get; }
//
//     /// <summary>
//     /// The processor checkpoint barrier position that triggered the last checkpoint.
//     /// </summary>
//     RecordPosition LastCheckpointId { get; }
//
//     /// <summary>
//     /// The active checkpoint barrier position being tracked.
//     /// </summary>
//     RecordPosition ActiveCheckpointId { get; }
//
//     /// <summary>
//     /// Tracks inflight position for future checkpointing.
//     /// </summary>
//     ValueTask TrackPosition(RecordPosition position);
//
//     /// <summary>
//     /// Triggers a checkpoint up to the specified checkpoint barrier, asynchronously (non-blocking).
//     /// </summary>
//     /// <remarks>
//     /// This function notifies the system that a checkpoint should be created
//     /// and can be called by the checkpointer itself or externally to create
//     /// Savepoints (manual undeletable checkpoints).
//     /// </remarks>
//     ValueTask TriggerCheckpoint(RecordPosition checkpointId, DateTimeOffset timestamp, CheckpointBarrierTrigger trigger = CheckpointBarrierTrigger.Savepoint);
//
//     /// <summary>
//     /// Tracks that a worker has processed up to the specified checkpoint barrier.
//     /// </summary>
//     ValueTask TrackCheckpoint(RecordPosition checkpointId, WorkerCheckpoint workerCheckpoint);
//
//     /// <summary>
//     /// Acknowledges that the checkpoint barrier has been enforced and all workers have processed up to it.
//     /// </summary>
//     ValueTask CommitCheckpoint(RecordPosition checkpointId);
// }
//
// public sealed record ProcessorCheckpointManagerState {
//     /// <summary>
//     /// The unique identifier for the processor.
//     /// </summary>
//     public string ProcessorId { get; init; } = "";
//
//     /// <summary>
//     /// The barrier position that represents the actual processor checkpoint.
//     /// When partitions are independently checkpointed, this value indicates
//     /// the barrier that triggered the checkpoint.
//     /// </summary>
//     public LogPosition CheckpointId { get; init; } = LogPosition.Unset;
//
//     /// <summary>
//     /// Worker checkpoints that contributed to this processor checkpoint.
//     /// </summary>
//     public WorkerCheckpoint[] WorkerCheckpoints { get; init; } = [];
//
//     /// <summary>
//     /// UTC timestamp when the checkpoint barrier was created.
//     /// </summary>
//     public DateTimeOffset CreateTime { get; init; } = DateTimeOffset.MinValue;
//
//     /// <summary>
//     /// UTC timestamp when the checkpoint barrier was acknowledged/committed.
//     /// </summary>
//     public DateTimeOffset AcknowledgeTime { get; init; } = DateTimeOffset.MinValue;
// }
//
// /// <summary>
// /// Represents a confirmed worker checkpoint for a given barrier position.
// /// </summary>
// public record WorkerCheckpoint {
//     /// <summary>
//     /// Worker that created the checkpoint.
//     /// </summary>
//     public string WorkerId { get; init; } = "";
//
//     /// <summary>
//     /// Individual partition checkpoints.
//     /// </summary>
//     public Dictionary<int, LogPosition> Positions { get; init; } = [];
//
//     /// <summary>
//     /// UTC timestamp when the checkpoint was acknowledged/accepted.
//     /// </summary>
//     public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;
// }
//
// [PublicAPI]
// public readonly record struct CheckpointBarrierRaised(
//     RecordPosition CheckpointId,
//     CheckpointBarrierTrigger Trigger,
//     DateTimeOffset Timestamp
// );
//
// /// <summary>
// /// Identifies the source of a checkpoint barrier trigger.
// /// </summary>
// public enum CheckpointBarrierTrigger {
//     /// <summary>
//     /// Barrier triggered on rebalancing events to ensure a consistent state across workers.
//     /// </summary>
//     Rebalance = 1,
//
//     /// <summary>
//     /// Barrier triggered on a periodic timer interval.
//     /// </summary>
//     Interval = 2,
//
//     /// <summary>
//     /// Barrier triggered by reaching a record count threshold.
//     /// </summary>
//     Threshold = 3,
//
//     /// <summary>
//     /// Barrier triggered by the processor reaching the end of the log (caught up).
//     /// </summary>
//     CaughtUp = 4,
//
//     /// <summary>
//     /// Commit triggered manually by the user via an explicit api call. (e.g., CreateCheckpoint)
//     /// </summary>
//     Savepoint = 5
// }
