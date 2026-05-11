using static System.DateTimeOffset;

namespace Kurrent.Processors.Checkpointing;

/// <summary>
/// A thread-safe Hybrid Logical Clock (HLC) that generates monotonically increasing timestamps
/// combining wall-clock time with a logical counter.
/// </summary>
/// <remarks>
/// Based on "Logical Physical Clocks" by Kulkarni et al.
/// Timestamps stay close to physical time while preserving causal ordering.
/// </remarks>
[PublicAPI]
public sealed class HybridLogicalClock {
    /// <summary>
    /// The default maximum tolerated clock offset (1 minute).
    /// </summary>
    public static readonly TimeSpan DefaultMaxOffset = TimeSpan.FromMinutes(1);

    readonly Lock         _lock = new();
    readonly TimeProvider _timeProvider;
    readonly TimeSpan     _maxOffset;

    int  _counter;
    long _physicalTime;

    /// <summary>
    /// Creates a new HLC seeded from the current wall-clock time.
    /// </summary>
    /// <param name="timeProvider">Time provider for wall-clock time. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="maxOffset">Maximum tolerated clock drift. Defaults to <see cref="DefaultMaxOffset"/>.</param>
    public HybridLogicalClock(TimeProvider? timeProvider = null, TimeSpan? maxOffset = null) {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxOffset    = maxOffset ?? DefaultMaxOffset;
        _physicalTime = GetWallClockTime();
        _counter      = 0;
    }

    /// <summary>
    /// Creates a new HLC synchronized to at least the given timestamp (e.g., restored from persisted state).
    /// </summary>
    /// <param name="initialTimestamp">The minimum starting timestamp.</param>
    /// <param name="timeProvider">Time provider for wall-clock time. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="maxOffset">Maximum tolerated clock drift. Defaults to <see cref="DefaultMaxOffset"/>.</param>
    public HybridLogicalClock(HybridTimestamp initialTimestamp, TimeProvider? timeProvider = null, TimeSpan? maxOffset = null) {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxOffset    = maxOffset ?? DefaultMaxOffset;

        var wallClock = GetWallClockTime();

        if (wallClock >= initialTimestamp.PhysicalTime) {
            _physicalTime = wallClock;
            _counter      = 0;
        }
        else {
            _physicalTime = initialTimestamp.PhysicalTime;
            _counter      = initialTimestamp.Counter;
        }
    }

    /// <summary>
    /// Gets the maximum tolerated clock offset.
    /// </summary>
    public TimeSpan MaxOffset => _maxOffset;

    /// <summary>
    /// Gets the current timestamp without advancing the clock.
    /// </summary>
    public HybridTimestamp Current {
        get {
            lock (_lock)
                return HybridTimestamp.Create(_physicalTime, _counter);
        }
    }

    /// <summary>
    /// Generates a new timestamp, advancing the clock.
    /// </summary>
    /// <returns>A new <see cref="HybridTimestamp"/> guaranteed to be greater than any previously returned timestamp.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the counter overflows due to excessive clock drift.</exception>
    public HybridTimestamp Now() {
        lock (_lock) {
            var wallClock = GetWallClockTime();

            if (wallClock > _physicalTime) {
                _physicalTime = wallClock;
                _counter      = 0;
            }

            var result = HybridTimestamp.Create(_physicalTime, _counter);

            _counter++;
            ValidateCounterOverflow(wallClock);

            return result;
        }
    }

    /// <summary>
    /// Updates the clock after observing an external timestamp, then advances and returns a new timestamp.
    /// </summary>
    /// <param name="received">The external timestamp to synchronize with.</param>
    /// <returns>A new <see cref="HybridTimestamp"/> greater than both the local clock and <paramref name="received"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the counter overflows due to excessive clock drift.</exception>
    /// <exception cref="ClockDriftException">
    /// Thrown when <paramref name="received"/> exceeds local physical time by more than <see cref="MaxOffset"/>.
    /// </exception>
    public HybridTimestamp Receive(HybridTimestamp received) {
        lock (_lock) {
            var wallClock = GetWallClockTime();

            ValidateClockDrift(received, wallClock);

            int resultCounter;
            if (wallClock > _physicalTime && wallClock > received.PhysicalTime) {
                _physicalTime = wallClock;
                resultCounter = 0;
            }
            else if (_physicalTime == received.PhysicalTime) {
                resultCounter = Math.Max(_counter, received.Counter) + 1;
            }
            else if (_physicalTime > received.PhysicalTime) {
                resultCounter = _counter + 1;
            }
            else {
                _physicalTime = received.PhysicalTime;
                resultCounter = received.Counter + 1;
            }

            var result = HybridTimestamp.Create(_physicalTime, resultCounter);

            _counter = resultCounter + 1;
            ValidateCounterOverflow(wallClock);

            return result;
        }
    }

    /// <summary>
    /// Ensures the clock is at least the given timestamp without advancing.
    /// </summary>
    /// <param name="timestamp">The timestamp to catch up to (e.g., restored from persisted state).</param>
    /// <exception cref="ClockDriftException">
    /// Thrown when <paramref name="timestamp"/> exceeds local physical time by more than <see cref="MaxOffset"/>.
    /// </exception>
    /// <remarks>
    /// Unlike <see cref="Receive"/>, this does not increment the counter - it only catches up if behind.
    /// </remarks>
    public void Sync(HybridTimestamp timestamp) {
        lock (_lock) {
            var wallClock = GetWallClockTime();

            ValidateClockDrift(timestamp, wallClock);

            if (timestamp.PhysicalTime > _physicalTime) {
                _physicalTime = timestamp.PhysicalTime;
                _counter      = timestamp.Counter;
            }
            else if (timestamp.PhysicalTime == _physicalTime && timestamp.Counter > _counter)
                _counter = timestamp.Counter;
            // else: our clock is already ahead, nothing to do
        }
    }

    long GetWallClockTime() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    void ValidateCounterOverflow(long wallClock) {
        if (_counter > ushort.MaxValue)
            throw new InvalidOperationException(
                $"HLC counter overflow - clock drift too high. PhysicalTime: {_physicalTime}, WallClock: {wallClock}");
    }

    void ValidateClockDrift(HybridTimestamp received, long wallClock) {
        var drift = received.PhysicalTime - wallClock;

        if (drift > _maxOffset.TotalMilliseconds)
            throw new ClockDriftException(
                $"Received timestamp {received} is {drift}ms ahead of wall clock. Maximum allowed offset is {_maxOffset.TotalMilliseconds}ms.");
    }
}

/// <summary>
/// A timestamp combining physical time (Unix millis) with a logical counter for ordering.
/// Encoded as 64-bit: upper 48 bits = physical time, lower 16 bits = counter.
/// </summary>
[PublicAPI]
public readonly record struct HybridTimestamp : IComparable<HybridTimestamp>, IComparable {
    /// <summary>
    /// Gets the physical time component (Unix time in milliseconds).
    /// </summary>
    public long PhysicalTime { get; private init; }

    /// <summary>
    /// Gets the counter component for ordering events within the same millisecond.
    /// </summary>
    public int Counter { get; private init; }

    /// <summary>
    /// Gets the packed 64-bit representation of this timestamp.
    /// </summary>
    public long Value { get; private init; }

    public DateTimeOffset DateTimeOffset { get; private init; }

    #region ->> IComparable <<-
    
    /// <inheritdoc />
    public int CompareTo(HybridTimestamp other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public int CompareTo(object? obj) =>
        obj is HybridTimestamp other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(HybridTimestamp)}");
    
    #endregion // IComparable
    
    #region ->> Relational Operators <<-
    /// <inheritdoc cref="IComparable{T}.CompareTo"/>
    public static bool operator <(HybridTimestamp left, HybridTimestamp right) => left.Value < right.Value;

    /// <inheritdoc cref="IComparable{T}.CompareTo"/>
    public static bool operator >(HybridTimestamp left, HybridTimestamp right) => left.Value > right.Value;

    /// <inheritdoc cref="IComparable{T}.CompareTo"/>
    public static bool operator <=(HybridTimestamp left, HybridTimestamp right) => left.Value <= right.Value;

    /// <inheritdoc cref="IComparable{T}.CompareTo"/>
    public static bool operator >=(HybridTimestamp left, HybridTimestamp right) => left.Value >= right.Value;

    #endregion // Relational Operators
    
    #region ->> Implicit/Explicit Conversions <<-
    
    /// <summary>
    /// Provides an implicit conversion from <see cref="HybridTimestamp"/> to a 64-bit integer value.
    /// </summary>
    public static implicit operator long(HybridTimestamp timestamp) =>
        timestamp.Value;

    /// <summary>
    /// Converts a 64-bit integer value into a <see cref="HybridTimestamp"/> instance explicitly.
    /// </summary>
    public static explicit operator HybridTimestamp(long value) =>
        Parse(value);

    /// <summary>
    /// Converts the <see cref="HybridTimestamp"/> instance to its string representation.
    /// </summary>
    public static implicit operator string(HybridTimestamp timestamp) =>
        timestamp.ToString();

    
    /// <summary>
    /// Parses a string representation of a timestamp to create a <see cref="HybridTimestamp"/> instance.
    /// The string should be in the format "{PhysicalTime}.{Counter}".
    /// </summary>
    public static implicit operator HybridTimestamp(string value) =>
        Parse(value);

    #endregion // Implicit/Explicit Conversions
    
    /// <inheritdoc />
    public override string ToString() => $"{PhysicalTime:D19}.{Counter:D5}";
    
    /// <summary>
    /// Creates a new timestamp with the specified physical time and counter.
    /// </summary>
    public static HybridTimestamp Create(long physicalTime, int counter) {
        ArgumentOutOfRangeException.ThrowIfNegative(physicalTime);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, ushort.MaxValue);

        return new HybridTimestamp {
            PhysicalTime   = physicalTime,
            Counter        = counter,
            Value          = (physicalTime << 16) | (uint)counter,
            DateTimeOffset =  FromUnixTimeMilliseconds(physicalTime)
        };
    }

    /// <summary>
    /// Creates a new timestamp with the specified physical time and counter.
    /// </summary>
    public static HybridTimestamp Create(DateTimeOffset physicalTimeUtc, int counter) {
        var physicalTime = physicalTimeUtc.ToUnixTimeMilliseconds();

        ArgumentOutOfRangeException.ThrowIfNegative(physicalTime);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, ushort.MaxValue);

        return new HybridTimestamp {
            PhysicalTime   = physicalTime,
            Counter        = counter,
            Value          = (physicalTime << 16) | (uint)counter,
            DateTimeOffset = physicalTimeUtc
        };
    }

    /// <summary>
    /// Attempts to parse a packed 64-bit value into a timestamp.
    /// </summary>
    public static bool TryParse(long value, out HybridTimestamp timestamp) {
        if (value < 0) {
            timestamp = default;
            return false;
        }

        var physicalTime = value >> 16;

        timestamp = new HybridTimestamp {
            PhysicalTime   = physicalTime,
            Counter        = (int)(value & 0xFFFF),
            Value          = value,
            DateTimeOffset = FromUnixTimeMilliseconds(physicalTime)
        };

        return true;
    }
    
    /// <summary>
    /// Parses a packed 64-bit value into a timestamp.
    /// </summary>
    public static HybridTimestamp Parse(long value) =>
        TryParse(value, out var hts) ? hts : throw new FormatException($"Value '{value}' is not a valid HybridTimestamp.");

    public static HybridTimestamp Parse(string value) =>
        long.TryParse(value, out var parsed) ? Parse(parsed) : throw new FormatException($"Value '{value}' is not a valid HybridTimestamp.");
}

/// <summary>
/// Thrown when a timestamp exceeds the maximum tolerated clock drift.
/// </summary>
[PublicAPI]
public sealed class ClockDriftException : Exception {
    /// <inheritdoc />
    public ClockDriftException() { }

    /// <inheritdoc />
    public ClockDriftException(string message) : base(message) { }

    /// <inheritdoc />
    public ClockDriftException(string message, Exception innerException) : base(message, innerException) { }
}
