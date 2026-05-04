namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// A thread-safe Lamport logical clock for ordering events in distributed systems.
/// </summary>
/// <remarks>
/// <para>
/// Lamport clocks provide a partial ordering of events where if event A causally precedes event B,
/// then the timestamp of A is less than the timestamp of B. However, the converse is not guaranteed.
/// </para>
/// <para>
/// The clock is seeded with the current Unix time in milliseconds to ensure timestamps
/// are always increasing across process restarts.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class LamportClock {
    ulong _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="LamportClock"/> class,
    /// seeded with the current Unix time in milliseconds.
    /// </summary>
    public LamportClock() : this((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LamportClock"/> class with the specified initial value.
    /// </summary>
    /// <param name="initialValue">The initial clock value, typically restored from persisted state.</param>
    public LamportClock(ulong initialValue) =>
        _value = initialValue;

    /// <summary>
    /// Gets the current clock value without advancing it.
    /// </summary>
    public ulong Current => Volatile.Read(ref _value);

    /// <summary>
    /// Advances the clock for a local event.
    /// </summary>
    /// <returns>The new clock value after incrementing.</returns>
    public ulong Tick() => Interlocked.Increment(ref _value);

    /// <summary>
    /// Updates the clock after witnessing a timestamp from another node.
    /// </summary>
    /// <param name="received">The timestamp received from another node.</param>
    /// <returns>The new clock value, guaranteed to be greater than both the local and received timestamps.</returns>
    /// <remarks>
    /// This implements the Lamport clock receive rule: <c>max(local, received) + 1</c>.
    /// </remarks>
    public ulong Witness(ulong received) {
        ulong current, updated;

        do {
            current = Volatile.Read(ref _value);
            updated = Math.Max(current, received) + 1;
        } while (Interlocked.CompareExchange(ref _value, updated, current) != current);

        return updated;
    }
}
