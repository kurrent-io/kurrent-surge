using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams;


// public readonly record struct Checkpoint(
//     string CheckpointId,
//     CheckpointTrigger Trigger,
//     DateTimeOffset Timestamp,
//     RecordPosition[] Positions
// );
//
//
// /// <summary>
// /// Identifies what triggered a checkpoint.
// /// </summary>
// /// <remarks>
// /// <para>
// /// Checkpoints can be triggered automatically by the server or manually by the user.
// /// The trigger is recorded with the checkpoint for diagnostics and auditing.
// /// </para>
// /// </remarks>
// [PublicAPI]
// public enum CheckpointTrigger {
//     /// <summary>
//     /// Unspecified checkpoint trigger.
//     /// </summary>
//     Unspecified = 0,
//
//     /// <summary>
//     /// Checkpoint triggered on rebalancing events to ensure a consistent state across drones.
//     /// </summary>
//     Rebalance = 1,
//
//     /// <summary>
//     /// Time interval elapsed since the last checkpoint.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Triggered automatically by the server based on a configured interval.
//     /// This is the primary mechanism for regular checkpointing.
//     /// </para>
//     /// <para>Frequency: Periodic (every interval milliseconds).</para>
//     /// <para>Auto-deleted: Yes (subject to retention count).</para>
//     /// </remarks>
//     Interval = 2,
//
//     /// <summary>
//     /// Subscription caught up to end of a filter, a stream or the log, transitioning to live mode.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Triggered exactly ONCE per processor lifecycle when the subscription
//     /// finishes processing historical data and transitions to receiving live
//     /// records. Only fires if the subscription started before the stream end
//     /// (i.e., had historical data to process).
//     /// </para>
//     /// <para>
//     /// This ensures that after processing a potentially large historical backlog,
//     /// the state is checkpointed before live processing begins. Prevents replaying
//     /// the entire history if the drone crashes shortly after going live.
//     /// </para>
//     /// <para>Frequency: Once per processor lifecycle (at catch-up completion).</para>
//     /// <para>Auto-deleted: Yes (subject to retention count).</para>
//     /// </remarks>
//     CaughtUp = 4,
//
//     /// <summary>
//     /// Checkpoint triggered manually by the user.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Triggered by an explicit API call to create a checkpoint, such as
//     /// <c>Processor.CreateCheckpointAsync()</c>. Used for scenarios where
//     /// the application needs to ensure a checkpoint at a specific point
//     /// in processing, outside the automatic mechanisms.
//     /// </para>
//     /// <para>Frequency: Manual (user-initiated).</para>
//     /// <para>Auto-deleted: No (retained until explicitly deleted).</para>
//     /// </remarks>
//     Savepoint = 5,
//
//     /// <summary>
//     /// Checkpoint triggered by graceful drone shutdown to persist final state.
//     /// </summary>
//     Shutdown = 6,
//
//     /// <summary>
//     /// Checkpoint triggered by manually setting a new high watermark position to start from.
//     /// </summary>
//     Reset = 7
// }


/// <summary>
/// Represents a heartbeat message providing information about the state or progress of processing.
/// </summary>
/// <param name="Type">
/// The type of the heartbeat, indicating the nature of the event (e.g., checkpoint or caught-up).
/// </param>
/// <param name="Position">
/// The position in the stream or sequence associated with this heartbeat.
/// </param>
/// <param name="Timestamp">
/// The timestamp indicating when this heartbeat was generated.
/// </param>
[PublicAPI]
[method: SetsRequiredMembers]
public readonly record struct Heartbeat(HeartbeatType Type, LogPosition Position, Revision Revision, DateTimeOffset Timestamp) {
	public static readonly Heartbeat Unset = new(HeartbeatType.Unspecified, LogPosition.Unset, Revision.Unset, DateTimeOffset.MinValue);

	public bool IsUnset => this == Unset;

    // temporary until I fix it...
    public RecordPosition RecordPosition =>
        HasPosition
            ? RecordPosition.From(Position)
            : RecordPosition.Unset;

	/// <summary>
	/// The type of the heartbeat, representing the nature of the event
	/// (e.g. indicating whether it is a checkpoint or a caught-up event).
	/// </summary>
	public required HeartbeatType Type { get; init; } = Type;

	/// <summary>
	/// The position in the stream or sequence associated with this heartbeat.
	/// This indicates the progress or location of the event in the processing sequence.
	/// </summary>
	public required LogPosition Position { get; init; } = Position;

	/// <summary>
	/// Represents a specific point in the stream or sequence.
	/// </summary>
	public required Revision Revision { get; init; } = Revision;

	/// <summary>
	/// The timestamp indicating when this heartbeat message was generated.
	/// </summary>
	public required DateTimeOffset Timestamp { get; init; } = Timestamp;

	/// <summary>
	/// Determines whether the heartbeat indicates a checkpoint event,
	/// signifying a specific milestone or save point in the processing sequence.
	/// </summary>
	public bool IsCheckpoint => Type == HeartbeatType.Checkpoint;

	/// <summary>
	/// Indicates whether the heartbeat represents a "CaughtUp" event, signalling that processing is up to date
	/// with the current state of the stream or sequence.
	/// </summary>
	public bool IsCaughtUp => Type == HeartbeatType.CaughtUp;

	public bool HasPosition => Position != LogPosition.Unset;

	public bool HasRevision => Revision != Revision.Unset;

	public override string ToString() =>
		$"Heartbeat {{ Type = {Type}, Position = {Position}, Revision = {Revision}, Timestamp = {Timestamp:O} }}";

	public static Heartbeat CreateCheckpoint(LogPosition position, DateTimeOffset timestamp) =>
		new(HeartbeatType.Checkpoint, position, Revision.Unset, timestamp);

	public static Heartbeat CreateCaughtUp(LogPosition position, DateTimeOffset timestamp) =>
		new(HeartbeatType.CaughtUp, position, Revision.Unset,  timestamp);

	public static Heartbeat CreateCheckpoint(LogPosition position, Revision streamRevision, DateTimeOffset timestamp) =>
		new(HeartbeatType.Checkpoint, position, streamRevision, timestamp);

	public static Heartbeat CreateCaughtUp(LogPosition position, Revision streamRevision, DateTimeOffset timestamp) =>
		new(HeartbeatType.CaughtUp, position, streamRevision, timestamp);
}

/// <summary>
/// Represents the type of heartbeat message.
/// A heartbeat indicates progress or a significant event in a sequence of operations.
/// </summary>
public enum HeartbeatType {
    Unspecified = 0,
    Checkpoint  = 1,
    CaughtUp    = 2,
}
