using Kurrent.Variant;

namespace Kurrent.Surge.Client.Streams;

// Checkpoint is a barrier (coordination token). Scan-progress cases (FilterCheckpoint,
// CaughtUpCheckpoint) carry a confirmed scan position; the client-side interval timer and
// processor-synthesized cases (IntervalCheckpoint, ManualCheckpoint, ShutdownCheckpoint, etc.)
// leave it empty.
// Streaming extends this hierarchy without touching client.
public abstract record Checkpoint(CheckpointId Id, DateTimeOffset Timestamp) {
    public IReadOnlyList<RecordPosition> Positions { get; init; } = [];
}

public sealed record IntervalCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

/// <summary>
/// Marks scan progress on a filtered subscription over the log: the scan has reached this log
/// position with no further matches in the window. Carries the scan watermark in
/// <see cref="Checkpoint.Positions"/>; paced by the subscription's max search window.
/// </summary>
/// <remarks>
/// Emitted by the server when the filter is expressed on the wire. When the filter cannot be —
/// and the SDK takes over filtering client-side — the SDK synthesizes these at the same cadence
/// so progress through barren stretches is never lost.
/// </remarks>
public sealed record FilterCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

public sealed record CaughtUpCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

/// <summary>
/// Represents a unique session identifier assigned by the server when a subscription is established.
/// </summary>
[PublicAPI]
public readonly record struct SessionId {
    /// <summary>
    /// Represents an unset session (e.g., drone never connected).
    /// </summary>
    public static readonly SessionId Unset = new(Guid.Empty);

    SessionId(Guid value) => Value = value;

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Indicates whether this session ID is unset.
    /// </summary>
    public bool IsUnset => this == Unset;

    /// <summary>
    /// Creates a <see cref="SessionId"/> from a GUID value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is <see cref="Guid.Empty"/>.</exception>
    public static SessionId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("SessionId cannot be empty.", nameof(value))
            : new(value);

    public static implicit operator Guid(SessionId _) => _.Value;
    public static implicit operator SessionId(Guid _) => From(_);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Unique identifier for a checkpoint.
/// </summary>
/// <remarks>
/// Checkpoint IDs are coordination tokens, not positions.
/// Drones use these IDs to identify which checkpoint they're responding to.
/// </remarks>
[PublicAPI]
public readonly record struct CheckpointId(string Value) {
    public static readonly CheckpointId Unset = new("");

    /// <summary>Creates a new unique checkpoint ID.</summary>
    public static CheckpointId New() => new(Guid.NewGuid().ToString());

    public static CheckpointId From(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CheckpointId(value);
    }

    public static CheckpointId From(ulong value) =>
        new(value.ToString());

    public static CheckpointId From(long value) =>
        new(value.ToString());

    /// <summary>True if this checkpoint ID is empty or unset.</summary>
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    public static implicit operator string(CheckpointId id) => id.Value;
}

public readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint> {
    public static readonly SubscribeMessage None;
}

public readonly record struct Connected(SessionId SessionId);

public class SubscribeOptions {
    public RecordFilter   Filter { get; set; } = RecordFilter.None;
    public RecordPosition Start  { get; set; } = RecordPosition.Latest;

    public bool SkipDecoding    { get; set; }
    public bool ResolveLinks    { get; set; } = true;
    public uint MaxSearchWindow { get; set; } = StreamDefaults.DefaultMaxSearchWindow;

    /// <summary>
    /// When true, a record whose schema name has no mapped type fails the subscription
    /// instead of passing through raw. Default false: a forward-unknown type is data for
    /// fallback handlers, not a crashed subscription. Has no effect when
    /// <see cref="SkipDecoding"/> is set.
    /// </summary>
    public bool ThrowOnUnresolved { get; set; }
}
