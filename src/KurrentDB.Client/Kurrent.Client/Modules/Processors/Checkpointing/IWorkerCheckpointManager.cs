// using System.Collections.Immutable;
// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Processors.Checkpointing;
//
// /// <summary>
// /// Manages checkpoint tracking and committing for a worker processing session.
// /// <para />
// /// This interface abstracts checkpoint operations, allowing the processing context
// /// to track and commit record positions without direct dependency on the session implementation.
// /// </summary>
// /// <remarks>
// /// When disabled, no automatic checkpointing occurs, and the application is responsible
// /// for managing checkpoints manually.
// /// </remarks>
// [PublicAPI]
// public interface IWorkerCheckpointManager {
//     /// <summary>
//     /// Settings for the worker checkpoint manager.
//     /// </summary>
//     CheckpointSettings Settings { get; }
//
//     /// <summary>
//     /// Currently tracked positions that have not yet been committed.
//     /// </summary>
//     ImmutableArray<RecordPosition> TrackedPositions { get; }
//
//     /// <summary>
//     /// The last position that was tracked.
//     /// </summary>
//     RecordPosition LastTrackedPosition { get; }
//
//     /// <summary>
//     /// The last position that was successfully committed.
//     /// </summary>
//     RecordPosition LastCheckpointPosition { get; }
//
//     /// <summary>
//     /// The reason for the last checkpoint.
//     /// </summary>
//     CheckpointTrigger LastCheckpointTrigger { get; }
//
//     /// <summary>
//     /// Tracks a position for later commit.
//     /// </summary>
//     ValueTask Track(RecordPosition position);
//
//     /// <summary>
//     /// Tracks a record's position for later commit.
//     /// </summary>
//     ValueTask Track(Record record) =>
//         record == Record.None || record.Position == RecordPosition.Unset
//             ? ValueTask.CompletedTask : Track(record.FullPosition);
//
//     /// <summary>
//     /// Commits all tracked positions to the persistence store, at that point in time.
//     /// </summary>
//     /// <param name="ct"></param>
//     /// <returns></returns>
//     ValueTask CommitAll(CancellationToken ct = default);
// }
// //
// // /// <summary>
// // /// Identifies what triggered a checkpoint.
// // /// </summary>
// // /// <remarks>
// // /// <para>
// // /// Checkpoints can be triggered automatically by the server or manually by the user.
// // /// The trigger is recorded with the checkpoint for diagnostics and auditing.
// // /// </para>
// // /// </remarks>
// // [PublicAPI]
// // public enum CheckpointTrigger {
// //     /// <summary>
// //     /// Unspecified checkpoint trigger.
// //     /// </summary>
// //     Unspecified = 0,
// //
// //     /// <summary>
// //     /// Barrier triggered on rebalancing events to ensure a consistent state across workers.
// //     /// </summary>
// //     Rebalance = 1,
// //
// //     /// <summary>
// //     /// Time interval elapsed since the last checkpoint.
// //     /// </summary>
// //     /// <remarks>
// //     /// <para>
// //     /// Triggered automatically by the server based on <see cref="CheckpointSettings.Interval"/>.
// //     /// This is the primary mechanism for regular checkpointing.
// //     /// </para>
// //     /// <para>Frequency: Periodic (every interval milliseconds).</para>
// //     /// <para>Auto-deleted: Yes (subject to retention count).</para>
// //     /// </remarks>
// //     Interval = 2,
// //
// //     /// <summary>
// //     /// Record count threshold reached.
// //     /// </summary>
// //     /// <remarks>
// //     /// <para>
// //     /// Triggered when <see cref="CheckpointSettings.Threshold"/> records have been processed
// //     /// since the last checkpoint. Useful for high-throughput scenarios where
// //     /// time-based intervals alone might result in too much data between checkpoints.
// //     /// </para>
// //     /// <para>Frequency: Periodic (every N records).</para>
// //     /// <para>Auto-deleted: Yes (subject to retention count).</para>
// //     /// </remarks>
// //     Threshold = 3,
// //
// //     /// <summary>
// //     /// Subscription caught up to end of log, transitioning to live mode.
// //     /// </summary>
// //     /// <remarks>
// //     /// <para>
// //     /// Triggered exactly ONCE per processor lifecycle when the subscription
// //     /// finishes processing historical data and transitions to receiving live
// //     /// records. Only fires if the subscription started before the stream end
// //     /// (i.e., had historical data to process).
// //     /// </para>
// //     /// <para>
// //     /// This ensures that after processing a potentially large historical backlog,
// //     /// the state is checkpointed before live processing begins. Prevents replaying
// //     /// the entire history if the worker crashes shortly after going live.
// //     /// </para>
// //     /// <para>Frequency: Once per processor lifecycle (at catch-up completion).</para>
// //     /// <para>Auto-deleted: Yes (subject to retention count).</para>
// //     /// </remarks>
// //     CaughtUp = 4,
// //
// //     /// <summary>
// //     /// Checkpoint triggered manually by the user.
// //     /// </summary>
// //     /// <remarks>
// //     /// <para>
// //     /// Triggered by an explicit API call to create a checkpoint, such as
// //     /// <c>Processor.CreateCheckpointAsync()</c>. Used for scenarios where
// //     /// the application needs to ensure a checkpoint at a specific point
// //     /// in processing, outside the automatic mechanisms.
// //     /// </para>
// //     /// <para>Frequency: Manual (user-initiated).</para>
// //     /// <para>Auto-deleted: No (retained until explicitly deleted).</para>
// //     /// </remarks>
// //     Savepoint = 5,
// //
// //     /// <summary>
// //     /// Server-side filter matched nothing for configured duration.
// //     /// </summary>
// //     /// <remarks>
// //     /// <para>
// //     /// Triggered when the server-side filter has not matched any records
// //     /// for the configured filter timeout period. This ensures checkpoints
// //     /// occur even when filtered streams have sparse matching records.
// //     /// </para>
// //     /// <para>Frequency: Periodic (after filter idle timeout).</para>
// //     /// <para>Auto-deleted: Yes (subject to retention count).</para>
// //     /// </remarks>
// //     FilterTimeout = 6
// // }
//
// // [PublicAPI]
// // public readonly record struct CheckpointBarrierRaised(
// //     RecordPosition CheckpointId,
// //     CheckpointTrigger Trigger,
// //     DateTimeOffset Timestamp );
