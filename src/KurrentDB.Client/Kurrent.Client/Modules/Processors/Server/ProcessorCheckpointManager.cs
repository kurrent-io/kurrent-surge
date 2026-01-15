// using System.Runtime.CompilerServices;
// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Processors.Checkpointing.Server;
//
// public class ProcessorCheckpointManager : IProcessorCheckpointManager , IAsyncDisposable  {
//     public ProcessorCheckpointManager(string processorId, Func<ValueTask<RecordPosition>> loadCheckpoint, Func<RecordPosition, ValueTask> storeCheckpoint, Func<CheckpointBarrierRaised, ValueTask> publish) {
//         ProcessorId     = processorId;
//         LoadCheckpoint  = loadCheckpoint;
//         StoreCheckpoint = storeCheckpoint;
//         Publish         = publish;
//         Tracker         = new PositionTracker();
//     }
//
//     PositionTracker                            Tracker         { get; }
//     Func<ValueTask<RecordPosition>>            LoadCheckpoint  { get; }
//     Func<RecordPosition, ValueTask>            StoreCheckpoint { get; }
//     Func<CheckpointBarrierRaised, ValueTask> Publish         { get; }
//
//     /// <summary>
//     /// The unique identifier for the processor.
//     /// </summary>
//     public string ProcessorId { get; }
//
//     /// <summary>
//     /// The current state of the processor checkpointer.
//     /// </summary>
//     public ProcessorCheckpointManagerState State { get; } = new ProcessorCheckpointManagerState();
//
//     /// <summary>
//     /// The processor checkpoint barrier position that triggered the last checkpoint.
//     /// </summary>
//     public RecordPosition LastCheckpointId { get; private set; } = RecordPosition.Unset;
//
//     /// <inheritdoc/>
//     public RecordPosition ActiveCheckpointId { get; private set; }
//
//
//     bool _initialized;
//
//     /// <summary>
//     /// Initializes the checkpointer by loading the last committed checkpoint position.
//     /// </summary>
//     /// <param name="ct"></param>
//     public async ValueTask Initialize(CancellationToken ct = default) {
//         // check if initialized using interlocked to ensure thread-safety
//         if (Interlocked.CompareExchange(ref Unsafe.As<bool, long>(ref _initialized), 1, 0) == 1)
//             return;
//
//         LastCheckpointId = await LoadCheckpoint().ConfigureAwait(false);
//     }
//
//     /// <summary>
//     /// Tracks inflight positions for future barrier checkpointing.
//     /// </summary>
//     public ValueTask TrackPosition(RecordPosition position) {
//         if (!_initialized)
//             throw new InvalidOperationException(
//                 "Checkpointer is not initialized. " +
//                 "Call Initialize() before triggering checkpoints.");
//
//         if (position == RecordPosition.Unset)
//             throw new ArgumentException("Position cannot be Unset", nameof(position));
//
//         if (ActiveCheckpointId != RecordPosition.Unset)
//             throw new InvalidOperationException(
//                 "Cannot track positions while a checkpoint barrier is active. " +
//                 "Complete or commit the active checkpoint barrier first.");
//
//
//         Tracker.Track(position);
//
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask TriggerCheckpoint(RecordPosition checkpointId, DateTimeOffset timestamp, CheckpointBarrierTrigger trigger = CheckpointBarrierTrigger.Savepoint) {
//         if (!_initialized)
//             throw new InvalidOperationException(
//                 "Checkpointer is not initialized. " +
//                 "Call Initialize() before triggering checkpoints.");
//
//         if (checkpointId == RecordPosition.Unset)
//             throw new ArgumentException("Checkpoint ID cannot be Unset", nameof(checkpointId));
//
//         if (ActiveCheckpointId != RecordPosition.Unset)
//             throw new InvalidOperationException(
//                 $"A checkpoint barrier is already active at ID {ActiveCheckpointId.LogPosition}." +
//                 $" Complete or commit it before triggering a new one.");
//
//         if (checkpointId <= ActiveCheckpointId)
//             throw new InvalidOperationException(
//                 $"Checkpoint ID {checkpointId.LogPosition} must be greater than active checkpoint ID {ActiveCheckpointId.LogPosition}");
//
//         ActiveCheckpointId = checkpointId;
//
//         var barrierRequired = new CheckpointBarrierRaised(
//             CheckpointId: checkpointId,
//             Trigger:      trigger,
//             Timestamp:    timestamp
//         );
//
//         return Publish(barrierRequired);
//     }
//
//     public ValueTask TrackCheckpoint(RecordPosition checkpointId, WorkerCheckpoint workerCheckpoint) {
//         if (!_initialized)
//             throw new InvalidOperationException(
//                 "Checkpointer is not initialized. " +
//                 "Call Initialize() before triggering checkpoints.");
//
//         if (checkpointId == RecordPosition.Unset)
//             throw new ArgumentException("Checkpoint ID cannot be Unset", nameof(checkpointId));
//
//         if (ActiveCheckpointId == RecordPosition.Unset)
//             throw new InvalidOperationException("No active checkpoint barrier. Trigger a checkpoint before tracking worker checkpoints.");
//
//         // TODO: store worker checkpoint state or event sourced ofc
//
//         return ValueTask.CompletedTask;
//     }
//
//     /// <summary>
//     /// Acknowledges that the processor has completed processing up to the specified checkpoint barrier.
//     /// </summary>
//     public async ValueTask CommitCheckpoint(RecordPosition checkpointId) {
//         if (!_initialized)
//             throw new InvalidOperationException(
//                 "Checkpointer is not initialized. " +
//                 "Call Initialize() before triggering checkpoints.");
//
//         if (checkpointId == RecordPosition.Unset)
//             throw new ArgumentException("Checkpoint ID cannot be Unset", nameof(checkpointId));
//
//         if (ActiveCheckpointId == RecordPosition.Unset)
//             throw new InvalidOperationException("No active checkpoint barrier. Trigger a checkpoint before committing.");
//
//         // TODO: store worker checkpoint state or event sourced ofc
//
//         // await StoreCheckpoint(checkpointId).ConfigureAwait(false);
//         //
//         // _ = Tracker.FlushUpTo(checkpointId);
//
//         LastCheckpointId = checkpointId;
//     }
//
//     /// <inheritdoc />
//     public ValueTask DisposeAsync() => throw new NotImplementedException();
// }
