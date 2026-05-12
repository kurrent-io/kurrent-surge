namespace Kurrent.Streaming.Checkpointing;

/// <summary>
/// Represents a Lamport timestamp with a process identifier for total ordering.
/// </summary>
/// <remarks>
/// <para>
/// This timestamp type provides total ordering across distributed processes by combining
/// the logical counter with a process identifier. When counters are equal, the process ID
/// serves as a deterministic tie-breaker.
/// </para>
/// <para>
/// The timestamp is encoded as a 64-bit value where the upper 48 bits represent
/// the counter and the lower 16 bits represent the process ID, following Lamport's
/// original paper suggestion of using <c>(counter, process_id)</c> tuples.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct ProcessLamportTimestamp : IComparable<ProcessLamportTimestamp>, IComparable {
    /// <summary>
    /// Gets the logical counter value.
    /// </summary>
    public long Counter { get; private init; }

    /// <summary>
    /// Gets the process identifier.
    /// </summary>
    public ushort ProcessId { get; private init; }

    /// <summary>
    /// Gets the packed 64-bit representation of this timestamp.
    /// </summary>
    public long Value { get; private init; }

    /// <summary>
    /// Creates a new <see cref="ProcessLamportTimestamp"/> with the specified counter and process ID.
    /// </summary>
    /// <param name="counter">The logical counter value. Must be non-negative and fit in 48 bits.</param>
    /// <param name="processId">The process identifier (0-65535).</param>
    /// <returns>A new <see cref="ProcessLamportTimestamp"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="counter"/> is negative or exceeds 48 bits.
    /// </exception>
    public static ProcessLamportTimestamp Create(long counter, ushort processId) {
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, 0x0000_FFFF_FFFF_FFFF);

        return new ProcessLamportTimestamp {
            Counter   = counter,
            ProcessId = processId,
            Value     = (counter << 16) | processId
        };
    }

    /// <summary>
    /// Parses a packed 64-bit value into a <see cref="ProcessLamportTimestamp"/>.
    /// </summary>
    /// <param name="value">The packed timestamp value. Must be non-negative.</param>
    /// <returns>A <see cref="ProcessLamportTimestamp"/> parsed from the value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static ProcessLamportTimestamp Parse(long value) {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        return new ProcessLamportTimestamp {
            Counter   = value >> 16,
            ProcessId = (ushort)(value & 0xFFFF),
            Value     = value
        };
    }

    /// <summary>
    /// Attempts to parse a packed 64-bit value into a <see cref="ProcessLamportTimestamp"/>.
    /// </summary>
    /// <param name="value">The packed timestamp value to parse.</param>
    /// <param name="timestamp">When this method returns, contains the parsed timestamp if successful; otherwise, the default value.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParse(long value, out ProcessLamportTimestamp timestamp) {
        if (value < 0) {
            timestamp = default;
            return false;
        }

        timestamp = new ProcessLamportTimestamp {
            Counter   = value >> 16,
            ProcessId = (ushort)(value & 0xFFFF),
            Value     = value
        };

        return true;
    }

    /// <inheritdoc />
    public int CompareTo(ProcessLamportTimestamp other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public int CompareTo(object? obj) =>
        obj is ProcessLamportTimestamp other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(ProcessLamportTimestamp)}");

    /// <summary>Determines whether one timestamp is less than another.</summary>
    public static bool operator <(ProcessLamportTimestamp left, ProcessLamportTimestamp right) =>
        left.Value < right.Value;

    /// <summary>Determines whether one timestamp is greater than another.</summary>
    public static bool operator >(ProcessLamportTimestamp left, ProcessLamportTimestamp right) =>
        left.Value > right.Value;

    /// <summary>Determines whether one timestamp is less than or equal to another.</summary>
    public static bool operator <=(ProcessLamportTimestamp left, ProcessLamportTimestamp right) =>
        left.Value <= right.Value;

    /// <summary>Determines whether one timestamp is greater than or equal to another.</summary>
    public static bool operator >=(ProcessLamportTimestamp left, ProcessLamportTimestamp right) =>
        left.Value >= right.Value;

    /// <summary>
    /// Converts a <see cref="ProcessLamportTimestamp"/> to its packed 64-bit representation.
    /// </summary>
    /// <param name="timestamp">The timestamp to convert.</param>
    public static implicit operator long(ProcessLamportTimestamp timestamp) => timestamp.Value;

    /// <summary>
    /// Converts a packed 64-bit value to a <see cref="ProcessLamportTimestamp"/>.
    /// </summary>
    /// <param name="value">The packed value to convert.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is negative.</exception>
    public static explicit operator ProcessLamportTimestamp(long value) => Parse(value);

    /// <inheritdoc />
    public override string ToString() => $"{Counter:D15}.{ProcessId:D5}";
}

/// <summary>
/// A thread-safe Lamport clock with process identifier for total ordering in distributed systems.
/// </summary>
/// <remarks>
/// <para>
/// This clock generates timestamps that include a process identifier, providing deterministic
/// total ordering across all processes. When two processes generate the same counter value,
/// the process ID serves as a tie-breaker.
/// </para>
/// <para>
/// Timestamps are packable into a single 64-bit value (48 bits counter + 16 bits process ID),
/// making them efficient for storage and transmission.
/// </para>
/// <para>
/// This follows Lamport's original paper suggestion of using <c>(counter, process_id)</c> tuples.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ProcessLamportClock {
    readonly ushort _processId;

    long _counter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessLamportClock"/> class
    /// with a random process ID, seeded with the current Unix time in milliseconds.
    /// </summary>
    public ProcessLamportClock() : this((ushort)Random.Shared.Next(ushort.MaxValue + 1)) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessLamportClock"/> class
    /// with the specified process ID, seeded with the current Unix time in milliseconds.
    /// </summary>
    /// <param name="processId">The unique identifier for this process (0-65535).</param>
    public ProcessLamportClock(ushort processId)
        : this(processId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessLamportClock"/> class
    /// with the specified process ID and initial counter value.
    /// </summary>
    /// <param name="processId">The unique identifier for this process (0-65535).</param>
    /// <param name="initialCounter">The initial counter value, typically restored from persisted state.</param>
    public ProcessLamportClock(ushort processId, long initialCounter) {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCounter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialCounter, 0x0000_FFFF_FFFF_FFFF);

        _processId = processId;
        _counter   = initialCounter;
    }

    /// <summary>
    /// Gets the process identifier for this clock.
    /// </summary>
    public ushort ProcessId => _processId;

    /// <summary>
    /// Gets the current timestamp without advancing the clock.
    /// </summary>
    public ProcessLamportTimestamp Current =>
        ProcessLamportTimestamp.Create(Volatile.Read(ref _counter), _processId);

    /// <summary>
    /// Advances the clock for a local event.
    /// </summary>
    /// <returns>The new timestamp after incrementing.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the counter overflows 48 bits.</exception>
    public ProcessLamportTimestamp Tick() {
        var newCounter = Interlocked.Increment(ref _counter);
        ValidateCounterOverflow(newCounter);
        return ProcessLamportTimestamp.Create(newCounter, _processId);
    }

    /// <summary>
    /// Updates the clock after witnessing a timestamp from another process.
    /// </summary>
    /// <param name="received">The timestamp received from another process.</param>
    /// <returns>A new timestamp guaranteed to be greater than both the local and received timestamps.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the counter overflows 48 bits.</exception>
    /// <remarks>
    /// This implements the Lamport clock receive rule: <c>max(local, received.Counter) + 1</c>.
    /// </remarks>
    public ProcessLamportTimestamp Witness(ProcessLamportTimestamp received) =>
        Witness(received.Counter);

    /// <summary>
    /// Updates the clock after witnessing a counter value from another process.
    /// </summary>
    /// <param name="receivedCounter">The counter value received from another process.</param>
    /// <returns>A new timestamp guaranteed to be greater than both the local and received counters.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the counter overflows 48 bits.</exception>
    public ProcessLamportTimestamp Witness(long receivedCounter) {
        long current, updated;

        do {
            current = Volatile.Read(ref _counter);
            updated = Math.Max(current, receivedCounter) + 1;
        } while (Interlocked.CompareExchange(ref _counter, updated, current) != current);

        ValidateCounterOverflow(updated);
        return ProcessLamportTimestamp.Create(updated, _processId);
    }

    static void ValidateCounterOverflow(long counter) {
        if (counter > 0x0000_FFFF_FFFF_FFFF)
            throw new InvalidOperationException($"Lamport counter overflow - exceeded 48-bit maximum. Counter: {counter}");
    }
}
