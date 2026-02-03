using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Kurrent.Variant;

namespace Kurrent.Client.Processors.Gateway.Messages;

/// <summary>
/// Unique identifier for checkpoint barriers, used to coordinate distributed checkpoints.
/// </summary>
/// <remarks>
/// Barrier IDs are coordination tokens, not positions. Each checkpoint barrier gets a unique GUID
/// to enable distributed checkpoint coordination across multiple workers (similar to Apache Flink).
/// Workers use these IDs to identify which checkpoint they're responding to.
/// </remarks>
[PublicAPI]
public readonly record struct BarrierId(string Value) {
	public static readonly BarrierId None = new(string.Empty);

	/// <summary>Creates a new unique barrier ID</summary>
	public static BarrierId New() => new(Guid.NewGuid().ToString());

    public static BarrierId From(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new BarrierId(value);
    }

    public static BarrierId From(ulong value) =>
        new(value.ToString());

    public static BarrierId From(long value) =>
        new(value.ToString());

    /// <summary>True if this barrier ID is empty or unset.</summary>
	public bool IsEmpty => string.IsNullOrEmpty(Value);

	public override string ToString() => Value;

	public static implicit operator string(BarrierId id) => id.Value;

    public static implicit operator ulong(BarrierId id) => ulong.Parse(id.Value);
}

#region Inbound - Control Messages

/// <summary>
/// Sent when partitions are assigned to this worker.
/// Worker MUST respond with <see cref="WorkerReady"/> before the server sends records.
/// </summary>
[PublicAPI]
public readonly record struct WorkOrder(
    ImmutableArray<Partition> Partitions
);

/// <summary>
/// A batch of records to process.
/// Only sent after worker acknowledges with <see cref="WorkerReady"/>.
/// </summary>
[PublicAPI]
public readonly record struct RecordBatch : IReadOnlyList<Record> {
    public static readonly RecordBatch Empty = new([]);

    readonly ImmutableArray<Record> _records;

    RecordBatch(ImmutableArray<Record> records) => _records = records;

    public static RecordBatch From(Record[] records) => records.ToImmutableArray();

    public int Count => _records.Length;

    public Record this[int index] => _records[index];

    public ImmutableArray<Record>.Enumerator GetEnumerator() => _records.GetEnumerator();

    IEnumerator<Record> IEnumerable<Record>.GetEnumerator() => _records.AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Record>)this).GetEnumerator();

    public static implicit operator RecordBatch(Record[] records)               => new([..records]);
    public static implicit operator RecordBatch(ImmutableArray<Record> records) => new(records);
}

/// <summary>
/// Maps partitions to their record positions, representing checkpoint state.
/// </summary>
[PublicAPI]
public readonly record struct PartitionPositions : IReadOnlyDictionary<Partition, RecordPosition> {
    public static readonly PartitionPositions Empty = new(ImmutableDictionary<Partition, RecordPosition>.Empty);

    readonly ImmutableDictionary<Partition, RecordPosition> _values;

    PartitionPositions(ImmutableDictionary<Partition, RecordPosition> values) => _values = values;

    public static PartitionPositions From(ImmutableDictionary<Partition, RecordPosition> values) => new(values);

    public RecordPosition MaxPosition => _values.IsEmpty ? RecordPosition.Unset : _values.Values.Max();

    public int Count => _values.Count;

    public RecordPosition this[Partition key] => _values[key];

    public IEnumerable<Partition> Keys => _values.Keys;

    IEnumerable<RecordPosition> IReadOnlyDictionary<Partition, RecordPosition>.Values => _values.Values;

    public bool ContainsKey(Partition key) => _values.ContainsKey(key);

    public bool TryGetValue(Partition key, out RecordPosition value) => _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<Partition, RecordPosition>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_values).GetEnumerator();

    public static implicit operator PartitionPositions(Dictionary<Partition, RecordPosition> values) => new(values.ToImmutableDictionary());

    public static implicit operator PartitionPositions(ImmutableDictionary<Partition, RecordPosition> values) => new(values);
}

/// <summary>
/// Signals the worker to snapshot state and respond with <see cref="CheckpointComplete"/>.
/// </summary>
[PublicAPI]
public readonly record struct CheckpointBarrier(
    BarrierId BarrierId,
    CheckpointTrigger Trigger,
    DateTimeOffset Timestamp
);

/// <summary>
/// Messages sent from the Processor Controller (server) to the worker.
/// </summary>
[PublicAPI]
public readonly partial record struct ProcessorControlMessage : IVariant<
    WorkOrder,
    RecordBatch,
    CheckpointBarrier> {
    public static readonly ProcessorControlMessage None;
}

#endregion

#region Outbound - Worker Messages

public readonly record struct Connect(ProcessorSettings Settings);

/// <summary>
/// Worker signals it has recovered to a consistent state and is ready to receive records.
/// Sent in response to <see cref="WorkOrder"/>.
/// A <see cref="RecordBatch"/> will not be sent until this message is received.
/// </summary>
/// <param name="BarrierId">The barrier ID representing the recovered checkpoint.</param>
/// <param name="Positions">Partition to record position mapping after recovery.</param>
[PublicAPI]
public readonly record struct WorkerReady(
    BarrierId BarrierId,
    PartitionPositions Positions
);

/// <summary>
/// Worker has completed checkpoint - positions are ready for persistence.
/// </summary>
/// <param name="BarrierId">The barrier ID this checkpoint responds to.</param>
/// <param name="Positions">Partition to record position mapping.</param>
[PublicAPI]
public readonly record struct CheckpointComplete(
    BarrierId BarrierId,
    PartitionPositions Positions
);

/// <summary>
/// Worker requests disconnect.
/// For graceful shutdown, includes final positions so relay can persist without barrier round-trip.
/// </summary>
/// <param name="Reason">Why the worker is disconnecting.</param>
/// <param name="Positions">Optional: final checkpoint positions of all partitions for graceful shutdown.</param>
[PublicAPI]
public readonly record struct Disconnect(
    DisconnectReason Reason,
    PartitionPositions Positions
);

/// <summary>
/// The messages a worker can send to the server. Outbound from worker to relay.
/// </summary>
[PublicAPI]
public readonly partial record struct WorkerMessage : IVariant<
    Connect,
    WorkerReady,
    CheckpointComplete,
    Disconnect> {
    public static readonly WorkerMessage None;
}

/// <summary>
/// Identifies what triggered a checkpoint.
/// </summary>
/// <remarks>
/// <para>
/// Checkpoints can be triggered automatically by the server or manually by the user.
/// The trigger is recorded with the checkpoint for diagnostics and auditing.
/// </para>
/// </remarks>
[PublicAPI]
public enum CheckpointTrigger {
    /// <summary>
    /// Unspecified checkpoint trigger.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Barrier triggered on rebalancing events to ensure a consistent state across workers.
    /// </summary>
    Rebalance = 1,

    /// <summary>
    /// Time interval elapsed since the last checkpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggered automatically by the server based on <see cref="CheckpointSettings.Interval"/>.
    /// This is the primary mechanism for regular checkpointing.
    /// </para>
    /// <para>Frequency: Periodic (every interval milliseconds).</para>
    /// <para>Auto-deleted: Yes (subject to retention count).</para>
    /// </remarks>
    Interval = 2,

    /// <summary>
    /// Subscription caught up to end of log, transitioning to live mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggered exactly ONCE per processor lifecycle when the subscription
    /// finishes processing historical data and transitions to receiving live
    /// records. Only fires if the subscription started before the stream end
    /// (i.e., had historical data to process).
    /// </para>
    /// <para>
    /// This ensures that after processing a potentially large historical backlog,
    /// the state is checkpointed before live processing begins. Prevents replaying
    /// the entire history if the worker crashes shortly after going live.
    /// </para>
    /// <para>Frequency: Once per processor lifecycle (at catch-up completion).</para>
    /// <para>Auto-deleted: Yes (subject to retention count).</para>
    /// </remarks>
    CaughtUp = 4,

    /// <summary>
    /// Checkpoint triggered manually by the user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggered by an explicit API call to create a checkpoint, such as
    /// <c>Processor.CreateCheckpointAsync()</c>. Used for scenarios where
    /// the application needs to ensure a checkpoint at a specific point
    /// in processing, outside the automatic mechanisms.
    /// </para>
    /// <para>Frequency: Manual (user-initiated).</para>
    /// <para>Auto-deleted: No (retained until explicitly deleted).</para>
    /// </remarks>
    Savepoint = 5
}

/// <summary>
/// Reason why the worker is requesting to be disconnected.
/// </summary>
[PublicAPI]
public enum DisconnectReason {
    /// <summary>
    /// Unspecified reason. For proto compatibility.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Graceful shutdown (SIGTERM, application stopping).
    /// </summary>
    Shutdown = 1,

    /// <summary>
    /// Worker encountered an error and must disconnect.
    /// If and when restarted, it will do so in a consistent state.
    /// </summary>
    Error = 2
}

#endregion
