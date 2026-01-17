using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Kurrent.Variant;

namespace Kurrent.Client.Processors.Gateway.Messages;

#region Inbound - Processor Control Messages

/// <summary>
/// Sent when partitions are assigned to this worker.
/// Worker MUST respond with <see cref="WorkerReady"/> before the server sends records.
/// </summary>
[PublicAPI]
public readonly record struct WorkOrder(
    // SessionId SessionId,
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

    public static RecordBatch From(ImmutableArray<Record> records) => new(records);

    public int Count => _records.Length;

    public Record this[int index] => _records[index];

    public ImmutableArray<Record>.Enumerator GetEnumerator() => _records.GetEnumerator();

    IEnumerator<Record> IEnumerable<Record>.GetEnumerator() => _records.AsEnumerable().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Record>)this).GetEnumerator();

    public static implicit operator RecordBatch(Record[] records)               => new([..records]);
    public static implicit operator RecordBatch(ImmutableArray<Record> records) => new(records);
}

/// <summary>
/// Maps partitions to their log positions, representing checkpoint state.
/// </summary>
[PublicAPI]
public readonly record struct PartitionPositions : IReadOnlyDictionary<Partition, LogPosition> {
    public static readonly PartitionPositions Empty = new(ImmutableDictionary<Partition, LogPosition>.Empty);

    readonly ImmutableDictionary<Partition, LogPosition> _values;

    PartitionPositions(ImmutableDictionary<Partition, LogPosition> values) => _values = values;

    public static PartitionPositions From(ImmutableDictionary<Partition, LogPosition> values) => new(values);

    public LogPosition MaxPosition => _values.IsEmpty ? LogPosition.Unset : _values.Values.Max();

    public int Count => _values.Count;

    public LogPosition this[Partition key] => _values[key];

    public IEnumerable<Partition> Keys => _values.Keys;

    IEnumerable<LogPosition> IReadOnlyDictionary<Partition, LogPosition>.Values => _values.Values;

    public bool ContainsKey(Partition key) => _values.ContainsKey(key);

    public bool TryGetValue(Partition key, out LogPosition value) => _values.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<Partition, LogPosition>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_values).GetEnumerator();

    public static implicit operator PartitionPositions(Dictionary<Partition, LogPosition> values) => new(values.ToImmutableDictionary());

    public static implicit operator PartitionPositions(ImmutableDictionary<Partition, LogPosition> values) => new(values);
}

/// <summary>
/// Signals the worker to snapshot state and respond with <see cref="CheckpointComplete"/>.
/// </summary>
[PublicAPI]
public readonly record struct CheckpointBarrier(
    LogPosition BarrierId,
    CheckpointBarrierTrigger Trigger,
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
/// <param name="BarrierId">The barrier ID representing the recovered checkpoint position.</param>
/// <param name="Positions">Partition to log position mapping after recovery.</param>
[PublicAPI]
public readonly record struct WorkerReady(
    LogPosition BarrierId,
    PartitionPositions Positions
);

/// <summary>
/// Worker has completed checkpoint - positions are ready for persistence.
/// </summary>
/// <param name="BarrierId">The barrier ID this checkpoint responds to.</param>
/// <param name="Positions">Partition to log position mapping.</param>
[PublicAPI]
public readonly record struct CheckpointComplete(
    LogPosition BarrierId,
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
) {
    public LogPosition BarrierId => Positions.MaxPosition;
}

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
public enum CheckpointBarrierTrigger {
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
    /// Record count threshold reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggered when <see cref="CheckpointSettings.Threshold"/> records have been processed
    /// since the last checkpoint. Useful for high-throughput scenarios where
    /// time-based intervals alone might result in too much data between checkpoints.
    /// </para>
    /// <para>Frequency: Periodic (every N records).</para>
    /// <para>Auto-deleted: Yes (subject to retention count).</para>
    /// </remarks>
    Threshold = 3,

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

    // /// <summary>
    // /// Server-side filter matched nothing for configured duration.
    // /// </summary>
    // /// <remarks>
    // /// <para>
    // /// Triggered when the server-side filter has not matched any records
    // /// for the configured filter timeout period. This ensures checkpoints
    // /// occur even when filtered streams have sparse matching records.
    // /// </para>
    // /// <para>Frequency: Periodic (after filter idle timeout).</para>
    // /// <para>Auto-deleted: Yes (subject to retention count).</para>
    // /// </remarks>
    // FilterTimeout = 6 // could this be modeled as a heartbeat instead? it does not matter if a filter is active or not...
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
