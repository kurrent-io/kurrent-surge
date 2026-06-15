using System.Collections;
using System.Collections.Immutable;
using Kurrent.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Options;
using Kurrent.Variant;

namespace Kurrent.Client.Processors.Gateway.Messages;

#region ->> Inbound - Control Messages <<-

/// <summary>
/// Sent when partitions are assigned to this drone.
/// Drone MUST respond with <see cref="DroneReady"/> before the server sends records.
/// </summary>
[PublicAPI]
public readonly record struct WorkOrder(
    SessionId        SessionId,
    RecordPosition[] Positions
);

/// <summary>
/// A batch of records to process.
/// Only sent after drone acknowledges with <see cref="DroneReady"/>.
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

    public static PartitionPositions From(RecordPosition[] positions) =>
        new(positions.ToImmutableDictionary(p => p.Partition, p => p));

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


// Processor-synthesized checkpoint cases extending client's Checkpoint hierarchy.
// Server-emitted cases (FilterCheckpoint, CaughtUpCheckpoint) live in the client.

public sealed record ManualCheckpoint(CheckpointId Id, DateTimeOffset Timestamp, string? Reason = null)
    : Checkpoint(Id, Timestamp);

public sealed record ShutdownCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

public sealed record RebalanceCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

static class CheckpointExtensions {
    extension(Checkpoint cp) {
        public CheckpointTrigger TriggerKind => cp switch {
            IntervalCheckpoint  => CheckpointTrigger.Interval,
            FilterCheckpoint    => CheckpointTrigger.Filter,
            CaughtUpCheckpoint  => CheckpointTrigger.CaughtUp,
            ManualCheckpoint    => CheckpointTrigger.Manual,
            ShutdownCheckpoint  => CheckpointTrigger.Shutdown,
            RebalanceCheckpoint => CheckpointTrigger.Rebalance,
            _                   => CheckpointTrigger.Unspecified
        };
    }
}

/// <summary>
/// Messages sent from the Processor Controller (server) to the drone.
/// </summary>
[PublicAPI]
public readonly partial record struct ProcessorControlMessage : IVariant<
    WorkOrder,
    Record,
    Checkpoint> {
    public static readonly ProcessorControlMessage None;
}

#endregion

#region ->> Outbound - Drone Messages <<-

/// <summary>
/// Drone signals it has recovered to a consistent state and is ready to receive records.
/// Sent in response to <see cref="WorkOrder"/>.
/// A <see cref="Record"/> will not be sent until this message is received.
/// </summary>
/// <param name="Positions">Partition to record position mapping after recovery.</param>
[PublicAPI]
public readonly record struct DroneReady(
    RecordPosition[] Positions
);

/// <summary>
/// Drone has completed checkpoint - positions are ready for persistence.
/// </summary>
/// <param name="CheckpointId">The checkpoint ID this completion responds to.</param>
/// <param name="Positions">Partition to record position mapping.</param>
[PublicAPI]
public readonly record struct CheckpointComplete(
    CheckpointId CheckpointId,
    RecordPosition[] Positions
);

/// <summary>
/// Drone requests disconnect.
/// For graceful shutdown, includes final positions so relay can persist without checkpoint round-trip.
/// </summary>
/// <param name="Reason">Why the drone is disconnecting.</param>
/// <param name="Positions">Optional: final checkpoint positions of all partitions for graceful shutdown.</param>
[PublicAPI]
public readonly record struct Disconnect(
    DisconnectReason Reason,
    RecordPosition[] Positions
);

/// <summary>
/// The messages a drone can send to the server. Outbound from drone to relay.
/// </summary>
[PublicAPI]
public readonly partial record struct DroneMessage : IVariant<
    DroneReady,
    CheckpointComplete,
    Disconnect> {
    public static readonly DroneMessage None;
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
    /// Checkpoint triggered on rebalancing events to ensure a consistent state across drones.
    /// </summary>
    Rebalance = 1,

    /// <summary>
    /// Time interval elapsed since the last checkpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Triggered by the client-side checkpointer timer based on <see cref="CheckpointOptions.Interval"/>.
    /// This is the primary mechanism for regular checkpointing.
    /// </para>
    /// <para>Frequency: Periodic (every interval milliseconds).</para>
    /// <para>Auto-deleted: Yes (subject to retention count).</para>
    /// </remarks>
    Interval = 2,

    /// <summary>
    /// The server's filtered log scan reached a new position with no matches in the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted by the server during a filtered subscription, paced by the subscription's max search
    /// window (one per scan window). Carries the scan watermark in the checkpoint's positions. When
    /// <c>AutoTrack</c> is enabled the drone folds it across every owned partition so a cold restart
    /// skips the already-scanned (no-match) region; when disabled the watermark is surfaced to
    /// <c>OnCheckpoint</c> as <c>ScanPosition</c> for the handler to act on.
    /// </para>
    /// <para>Frequency: Periodic (every scan window while catching up over a sparse filter).</para>
    /// </remarks>
    Filter = 3,

    /// <summary>
    /// Subscription caught up to end of a filter, a stream or the log, transitioning to live mode.
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
    /// the entire history if the drone crashes shortly after going live.
    /// </para>
    /// <para>Frequency: Once per processor lifecycle (at catch-up completion).</para>
    /// <para>Auto-deleted: Yes (subject to retention count).</para>
    /// </remarks>
    CaughtUp = 4,

    /// <summary>
    /// Checkpoint triggered manually by the user from inside <c>ProcessRecord</c>
    /// via <c>ctx.Checkpoint(reason)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The optional <c>Reason</c> on the <see cref="Checkpoint"/> message carries
    /// any string the user passed to <c>ctx.Checkpoint(reason)</c>. User code typically
    /// branches on <see cref="Trigger"/> and/or <c>Reason</c> in <c>OnCheckpoint</c> to
    /// gate or shape commit behavior.
    /// </para>
    /// <para>Frequency: Manual (user-initiated, at most one per <c>ProcessRecord</c> invocation).</para>
    /// </remarks>
    Manual = 5,

    /// <summary>
    /// Checkpoint triggered by graceful drone shutdown to persist final state.
    /// </summary>
    Shutdown = 6,

    /// <summary>
    /// Checkpoint triggered by manually setting a new high watermark position to start from.
    /// </summary>
    Reset = 7
}

/// <summary>
/// Reason why the drone is requesting to be disconnected.
/// </summary>
[PublicAPI]
public enum DisconnectReason {
    Unspecified = 0,

    /// <summary>
    /// Graceful stop (SIGTERM, cancellation, application stopping).
    /// </summary>
    Stopped = 1,

    /// <summary>
    /// Drone encountered an error and must disconnect.
    /// If and when restarted, it will do so in a consistent state.
    /// </summary>
    Error = 2
}

#endregion
