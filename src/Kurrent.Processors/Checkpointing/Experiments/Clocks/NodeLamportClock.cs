namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// Represents a Lamport timestamp with a UUID node identifier for total ordering.
/// </summary>
/// <remarks>
/// <para>
/// This timestamp type provides total ordering across distributed nodes by combining
/// the logical counter with a unique node identifier (UUID). When counters are equal,
/// the node ID serves as a deterministic tie-breaker.
/// </para>
/// <para>
/// This approach is similar to Cassandra's conflict resolution strategy.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct NodeLamportTimestamp : IComparable<NodeLamportTimestamp>, IComparable {
    /// <summary>
    /// Gets the logical counter value.
    /// </summary>
    public ulong Counter { get; private init; }

    /// <summary>
    /// Gets the unique node identifier.
    /// </summary>
    public Guid NodeId { get; private init; }

    /// <summary>
    /// Creates a new <see cref="NodeLamportTimestamp"/> with the specified counter and node ID.
    /// </summary>
    /// <param name="counter">The logical counter value.</param>
    /// <param name="nodeId">The unique node identifier.</param>
    /// <returns>A new <see cref="NodeLamportTimestamp"/> instance.</returns>
    public static NodeLamportTimestamp Create(ulong counter, Guid nodeId) =>
        new() { Counter = counter, NodeId = nodeId };

    /// <inheritdoc />
    public int CompareTo(NodeLamportTimestamp other) {
        var counterCmp = Counter.CompareTo(other.Counter);
        return counterCmp != 0 ? counterCmp : NodeId.CompareTo(other.NodeId);
    }

    /// <inheritdoc />
    public int CompareTo(object? obj) =>
        obj is NodeLamportTimestamp other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(NodeLamportTimestamp)}");

    /// <summary>Determines whether one timestamp is less than another.</summary>
    public static bool operator <(NodeLamportTimestamp left, NodeLamportTimestamp right) =>
        left.CompareTo(right) < 0;

    /// <summary>Determines whether one timestamp is greater than another.</summary>
    public static bool operator >(NodeLamportTimestamp left, NodeLamportTimestamp right) =>
        left.CompareTo(right) > 0;

    /// <summary>Determines whether one timestamp is less than or equal to another.</summary>
    public static bool operator <=(NodeLamportTimestamp left, NodeLamportTimestamp right) =>
        left.CompareTo(right) <= 0;

    /// <summary>Determines whether one timestamp is greater than or equal to another.</summary>
    public static bool operator >=(NodeLamportTimestamp left, NodeLamportTimestamp right) =>
        left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Counter:D20}:{NodeId}";
}

/// <summary>
/// A thread-safe Lamport clock with UUID node identifier for total ordering in distributed systems.
/// </summary>
/// <remarks>
/// <para>
/// This clock generates timestamps that include a unique node identifier (UUID),
/// providing deterministic total ordering across all nodes in a distributed system.
/// When two nodes generate the same counter value, the node ID serves as a tie-breaker.
/// </para>
/// <para>
/// This approach is similar to Cassandra's timestamp-based conflict resolution.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class NodeLamportClock {
    readonly Guid _nodeId;

    ulong _counter;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeLamportClock"/> class
    /// with a new random node ID, seeded with the current Unix time in milliseconds.
    /// </summary>
    public NodeLamportClock() : this(Guid.NewGuid()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeLamportClock"/> class
    /// with the specified node ID, seeded with the current Unix time in milliseconds.
    /// </summary>
    /// <param name="nodeId">The unique identifier for this node.</param>
    public NodeLamportClock(Guid nodeId)
        : this(nodeId, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeLamportClock"/> class
    /// with the specified node ID and initial counter value.
    /// </summary>
    /// <param name="nodeId">The unique identifier for this node.</param>
    /// <param name="initialCounter">The initial counter value, typically restored from persisted state.</param>
    public NodeLamportClock(Guid nodeId, ulong initialCounter) {
        _nodeId  = nodeId;
        _counter = initialCounter;
    }

    /// <summary>
    /// Gets the unique identifier for this node.
    /// </summary>
    public Guid NodeId => _nodeId;

    /// <summary>
    /// Gets the current timestamp without advancing the clock.
    /// </summary>
    public NodeLamportTimestamp Current =>
        NodeLamportTimestamp.Create(Volatile.Read(ref _counter), _nodeId);

    /// <summary>
    /// Advances the clock for a local event.
    /// </summary>
    /// <returns>The new timestamp after incrementing.</returns>
    public NodeLamportTimestamp Tick() =>
        NodeLamportTimestamp.Create(Interlocked.Increment(ref _counter), _nodeId);

    /// <summary>
    /// Updates the clock after witnessing a timestamp from another node.
    /// </summary>
    /// <param name="received">The timestamp received from another node.</param>
    /// <returns>A new timestamp guaranteed to be greater than both the local and received timestamps.</returns>
    /// <remarks>
    /// This implements the Lamport clock receive rule: <c>max(local, received.Counter) + 1</c>.
    /// </remarks>
    public NodeLamportTimestamp Witness(NodeLamportTimestamp received) {
        ulong current, updated;

        do {
            current = Volatile.Read(ref _counter);
            updated = Math.Max(current, received.Counter) + 1;
        } while (Interlocked.CompareExchange(ref _counter, updated, current) != current);

        return NodeLamportTimestamp.Create(updated, _nodeId);
    }

    /// <summary>
    /// Updates the clock after witnessing a counter value from another node.
    /// </summary>
    /// <param name="receivedCounter">The counter value received from another node.</param>
    /// <returns>A new timestamp guaranteed to be greater than both the local and received counters.</returns>
    public NodeLamportTimestamp Witness(ulong receivedCounter) {
        ulong current, updated;

        do {
            current = Volatile.Read(ref _counter);
            updated = Math.Max(current, receivedCounter) + 1;
        } while (Interlocked.CompareExchange(ref _counter, updated, current) != current);

        return NodeLamportTimestamp.Create(updated, _nodeId);
    }
}
