using System.Globalization;

using static System.DateTimeOffset;

namespace Kurrent.Surge.Processors.Checkpointing;

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
    /// <exception cref="ClockDriftException">
    /// Thrown when <paramref name="initialTimestamp"/> exceeds local physical time by more than <paramref name="maxOffset"/>.
    /// </exception>
    public HybridLogicalClock(HybridTimestamp initialTimestamp, TimeProvider? timeProvider = null, TimeSpan? maxOffset = null) {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxOffset    = maxOffset ?? DefaultMaxOffset;

        var wallClock = GetWallClockTime();

        // Same guard as Sync: a persisted timestamp this far ahead indicates a corrupted
        // checkpoint or misconfigured clock - seeding from it would drag every subsequently
        // issued timestamp into the future.
        ValidateClockDrift(initialTimestamp, wallClock);

        // The restored timestamp was already issued once, so when the wall clock has not
        // moved past its millisecond the next counter must start beyond it - seeding the
        // counter verbatim would make the first Now() re-issue the persisted timestamp.
        if (wallClock > initialTimestamp.PhysicalTime) {
            _physicalTime = wallClock;
            _counter      = 0;
        }
        else {
            _physicalTime = initialTimestamp.PhysicalTime;
            _counter      = initialTimestamp.Counter + 1;
        }
    }

    /// <summary>
    /// Gets the maximum tolerated clock offset.
    /// </summary>
    public TimeSpan MaxOffset => _maxOffset;

    /// <summary>
    /// Gets the timestamp the clock would issue next, without advancing the clock.
    /// </summary>
    /// <remarks>
    /// This is an observation of clock state, not an issued timestamp - the next call to
    /// <see cref="Now"/> may return an equal value. Never persist it as if it were issued.
    /// </remarks>
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
    /// <exception cref="CounterOverflowException">Thrown when the counter overflows due to excessive clock drift.</exception>
    public HybridTimestamp Now() {
        lock (_lock) {
            var wallClock = GetWallClockTime();

            if (wallClock > _physicalTime) {
                _physicalTime = wallClock;
                _counter      = 0;
            }

            // Validate before Create so exhaustion surfaces as the documented
            // InvalidOperationException instead of Create's ArgumentOutOfRangeException.
            ValidateCounterOverflow(_counter, wallClock);

            var result = HybridTimestamp.Create(_physicalTime, _counter);

            _counter++;

            return result;
        }
    }

    /// <summary>
    /// Updates the clock after observing an external timestamp, then advances and returns a new timestamp.
    /// </summary>
    /// <param name="received">The external timestamp to synchronize with.</param>
    /// <returns>A new <see cref="HybridTimestamp"/> greater than both the local clock and <paramref name="received"/>.</returns>
    /// <exception cref="CounterOverflowException">Thrown when the counter overflows due to excessive clock drift.</exception>
    /// <exception cref="ClockDriftException">
    /// Thrown when <paramref name="received"/> exceeds local physical time by more than <see cref="MaxOffset"/>.
    /// </exception>
    public HybridTimestamp Receive(HybridTimestamp received) {
        lock (_lock) {
            var wallClock = GetWallClockTime();

            ValidateClockDrift(received, wallClock);

            // Counter rules follow the paper's receive event (Kulkarni et al., Algorithm 2),
            // where c is the LAST ISSUED counter. _counter holds the NEXT counter to issue
            // (= last issued + 1), so the paper's "+1" is already baked into it - adding
            // another would skip a counter value on every receive and burn the 16-bit
            // range twice as fast under a stuck wall clock.
            int resultCounter;
            if (wallClock > _physicalTime && wallClock > received.PhysicalTime) {
                _physicalTime = wallClock;
                resultCounter = 0;
            }
            else if (_physicalTime == received.PhysicalTime) {
                // Paper: c = max(local last issued, received) + 1 = max(_counter, received.Counter + 1).
                resultCounter = Math.Max(_counter, received.Counter + 1);
            }
            else if (_physicalTime > received.PhysicalTime) {
                // Paper: c = local last issued + 1, which is exactly the next-to-issue counter.
                resultCounter = _counter;
            }
            else {
                _physicalTime = received.PhysicalTime;
                resultCounter = received.Counter + 1;
            }

            // Validate before Create so exhaustion surfaces as the documented
            // InvalidOperationException instead of Create's ArgumentOutOfRangeException.
            ValidateCounterOverflow(resultCounter, wallClock);

            var result = HybridTimestamp.Create(_physicalTime, resultCounter);

            _counter = resultCounter + 1;

            return result;
        }
    }

    /// <summary>
    /// Ensures all subsequently issued timestamps are greater than the given timestamp, without issuing one.
    /// </summary>
    /// <param name="timestamp">The timestamp to catch up past (e.g., restored from persisted state).</param>
    /// <exception cref="ClockDriftException">
    /// Thrown when <paramref name="timestamp"/> exceeds local physical time by more than <see cref="MaxOffset"/>.
    /// </exception>
    /// <remarks>
    /// Unlike <see cref="Receive"/>, this does not issue a timestamp - it only catches up if behind.
    /// </remarks>
    public void Sync(HybridTimestamp timestamp) {
        lock (_lock) {
            var wallClock = GetWallClockTime();

            ValidateClockDrift(timestamp, wallClock);

            // The synced timestamp was already issued elsewhere (persisted state), so the
            // next locally issued counter must start beyond it, never at it.
            if (timestamp.PhysicalTime > _physicalTime) {
                _physicalTime = timestamp.PhysicalTime;
                _counter      = timestamp.Counter + 1;
            }
            else if (timestamp.PhysicalTime == _physicalTime && timestamp.Counter >= _counter)
                _counter = timestamp.Counter + 1;
            // else: our clock is already ahead, nothing to do
        }
    }

    long GetWallClockTime() => _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    void ValidateCounterOverflow(int counter, long wallClock) {
        if (counter > ushort.MaxValue)
            throw new CounterOverflowException(
                $"HLC counter overflow - clock drift too high. PhysicalTime: {_physicalTime}, WallClock: {wallClock}");
    }

    void ValidateClockDrift(HybridTimestamp timestamp, long wallClock) {
        var drift = timestamp.PhysicalTime - wallClock;

        if (drift > _maxOffset.TotalMilliseconds)
            throw new ClockDriftException(
                $"Timestamp {timestamp} is {drift}ms ahead of wall clock. Maximum allowed offset is {_maxOffset.TotalMilliseconds}ms.");
    }
}

/// <summary>
/// A timestamp combining physical time (Unix millis) with a logical counter for ordering.
/// Encoded as 64-bit: 47 usable bits of physical time, lower 16 bits = counter.
/// </summary>
[PublicAPI]
public readonly record struct HybridTimestamp :
    IComparable<HybridTimestamp>, IComparable,
    ISpanFormattable, IUtf8SpanFormattable,
    ISpanParsable<HybridTimestamp>, IUtf8SpanParsable<HybridTimestamp> {
    /// <summary>
    /// The maximum physical time (Unix millis) that fits in the packed representation.
    /// </summary>
    /// <remarks>
    /// The lower 16 bits of the packed value hold the counter; the physical time occupies
    /// the remaining 47 usable bits, keeping the sign bit clear so packed values order
    /// correctly as plain longs.
    /// </remarks>
    public const long MaxPhysicalTime = (1L << 47) - 1;

    // Canonical text form is fixed-width and culture-invariant: 19 digits of physical
    // time, '.', 5 digits of counter. Fixed width keeps the string form lexicographically
    // sortable in the same order as the packed numeric value.
    const int FormattedLength = 25;

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
    public long Value => (PhysicalTime << 16) | (uint)Counter;

    /// <summary>
    /// Gets the physical time component as a UTC <see cref="System.DateTimeOffset"/>, at millisecond precision.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="PhysicalTime"/> so it never carries sub-millisecond ticks
    /// or a non-zero offset, keeping record equality consistent across creation paths.
    /// </remarks>
    public DateTimeOffset DateTimeOffset => FromUnixTimeMilliseconds(PhysicalTime);

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
    /// Converts the <see cref="HybridTimestamp"/> instance to its canonical string representation.
    /// </summary>
    /// <remarks>
    /// Implicit on purpose: the conversion is lossless and never throws, and checkpoint
    /// identifiers are constructed directly from timestamps via this conversion. The
    /// string-to-timestamp direction can fail, so it is explicit.
    /// </remarks>
    public static implicit operator string(HybridTimestamp timestamp) =>
        timestamp.ToString();

    /// <summary>
    /// Parses the canonical string representation ("{PhysicalTime:D19}.{Counter:D5}") into a <see cref="HybridTimestamp"/> instance.
    /// </summary>
    public static explicit operator HybridTimestamp(string value) =>
        Parse(value);

    #endregion // Implicit/Explicit Conversions
    
    /// <inheritdoc />
    public override string ToString() =>
        string.Create(FormattedLength, this, static (span, ts) => ts.TryFormat(span, out _, default, null));

    /// <inheritdoc />
    /// <remarks>The canonical form is culture-invariant; <paramref name="format"/> and <paramref name="formatProvider"/> are ignored.</remarks>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <inheritdoc />
    /// <remarks>The canonical form is culture-invariant; <paramref name="format"/> and <paramref name="provider"/> are ignored.</remarks>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) {
        if (destination.Length < FormattedLength) {
            charsWritten = 0;
            return false;
        }

        PhysicalTime.TryFormat(destination, out _, "D19", CultureInfo.InvariantCulture);
        destination[19] = '.';
        Counter.TryFormat(destination[20..], out _, "D5", CultureInfo.InvariantCulture);

        charsWritten = FormattedLength;
        return true;
    }

    /// <inheritdoc />
    /// <remarks>The canonical form is culture-invariant; <paramref name="format"/> and <paramref name="provider"/> are ignored.</remarks>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) {
        if (utf8Destination.Length < FormattedLength) {
            bytesWritten = 0;
            return false;
        }

        PhysicalTime.TryFormat(utf8Destination, out _, "D19", CultureInfo.InvariantCulture);
        utf8Destination[19] = (byte)'.';
        Counter.TryFormat(utf8Destination[20..], out _, "D5", CultureInfo.InvariantCulture);

        bytesWritten = FormattedLength;
        return true;
    }
    
    /// <summary>
    /// Creates a new timestamp with the specified physical time and counter.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="physicalTime"/> is negative or exceeds <see cref="MaxPhysicalTime"/>,
    /// or when <paramref name="counter"/> is negative or exceeds <see cref="ushort.MaxValue"/>.
    /// </exception>
    public static HybridTimestamp Create(long physicalTime, int counter) {
        ArgumentOutOfRangeException.ThrowIfNegative(physicalTime);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(physicalTime, MaxPhysicalTime);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(counter, ushort.MaxValue);

        return new HybridTimestamp {
            PhysicalTime = physicalTime,
            Counter      = counter
        };
    }

    /// <summary>
    /// Creates a new timestamp from the given instant, truncated to millisecond precision, and counter.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="physicalTimeUtc"/> is before the Unix epoch or exceeds <see cref="MaxPhysicalTime"/>,
    /// or when <paramref name="counter"/> is negative or exceeds <see cref="ushort.MaxValue"/>.
    /// </exception>
    public static HybridTimestamp Create(DateTimeOffset physicalTimeUtc, int counter) =>
        Create(physicalTimeUtc.ToUnixTimeMilliseconds(), counter);

    /// <summary>
    /// Attempts to parse a packed 64-bit value into a timestamp.
    /// </summary>
    public static bool TryParse(long value, out HybridTimestamp timestamp) {
        if (value < 0) {
            timestamp = default;
            return false;
        }

        timestamp = new HybridTimestamp {
            PhysicalTime = value >> 16,
            Counter      = (int)(value & 0xFFFF)
        };

        return true;
    }
    
    /// <summary>
    /// Parses a packed 64-bit value into a timestamp.
    /// </summary>
    public static HybridTimestamp Parse(long value) =>
        TryParse(value, out var hts) ? hts : throw new FormatException($"Value '{value}' is not a valid HybridTimestamp.");

    /// <summary>
    /// Parses the canonical string representation ("{PhysicalTime:D19}.{Counter:D5}") into a timestamp.
    /// </summary>
    public static HybridTimestamp Parse(string value) =>
        Parse(value.AsSpan());

    /// <summary>
    /// Parses the canonical string representation ("{PhysicalTime:D19}.{Counter:D5}") into a timestamp.
    /// </summary>
    public static HybridTimestamp Parse(ReadOnlySpan<char> value) =>
        TryParse(value, out var hts) ? hts : throw new FormatException($"Value '{value}' is not a valid HybridTimestamp.");

    /// <summary>
    /// Attempts to parse the canonical string representation ("{PhysicalTime:D19}.{Counter:D5}") into a timestamp.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> value, out HybridTimestamp timestamp) {
        timestamp = default;

        if (value.Length != FormattedLength || value[19] != '.')
            return false;

        // NumberStyles.None rejects signs, whitespace and group separators, so the
        // fixed-width digit groups are the only accepted form.
        if (!long.TryParse(value[..19], NumberStyles.None, CultureInfo.InvariantCulture, out var physicalTime) ||
            !int.TryParse(value[20..], NumberStyles.None, CultureInfo.InvariantCulture, out var counter))
            return false;

        if (physicalTime > MaxPhysicalTime || counter > ushort.MaxValue)
            return false;

        timestamp = Create(physicalTime, counter);
        return true;
    }

    /// <summary>
    /// Parses the canonical UTF-8 representation ("{PhysicalTime:D19}.{Counter:D5}") into a timestamp.
    /// </summary>
    public static HybridTimestamp Parse(ReadOnlySpan<byte> utf8Value) =>
        TryParse(utf8Value, out var hts) ? hts : throw new FormatException("Value is not a valid HybridTimestamp.");

    /// <summary>
    /// Attempts to parse the canonical UTF-8 representation ("{PhysicalTime:D19}.{Counter:D5}") into a timestamp.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8Value, out HybridTimestamp timestamp) {
        timestamp = default;

        if (utf8Value.Length != FormattedLength || utf8Value[19] != (byte)'.')
            return false;

        // NumberStyles.None rejects signs, whitespace and group separators, so the
        // fixed-width digit groups are the only accepted form.
        if (!long.TryParse(utf8Value[..19], NumberStyles.None, CultureInfo.InvariantCulture, out var physicalTime) ||
            !int.TryParse(utf8Value[20..], NumberStyles.None, CultureInfo.InvariantCulture, out var counter))
            return false;

        if (physicalTime > MaxPhysicalTime || counter > ushort.MaxValue)
            return false;

        timestamp = Create(physicalTime, counter);
        return true;
    }

    #region ->> Parsable Interface Implementations <<-

    static HybridTimestamp IParsable<HybridTimestamp>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<HybridTimestamp>.TryParse(string? s, IFormatProvider? provider, out HybridTimestamp result) =>
        TryParse(s.AsSpan(), out result);

    static HybridTimestamp ISpanParsable<HybridTimestamp>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<HybridTimestamp>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out HybridTimestamp result) =>
        TryParse(s, out result);

    static HybridTimestamp IUtf8SpanParsable<HybridTimestamp>.Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text);

    static bool IUtf8SpanParsable<HybridTimestamp>.TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out HybridTimestamp result) =>
        TryParse(utf8Text, out result);

    #endregion // Parsable Interface Implementations
}

/// <summary>
/// Thrown when the logical counter exhausts its 16-bit range because the wall clock
/// is stuck or lagging too far behind the clock's physical component.
/// </summary>
[PublicAPI]
public sealed class CounterOverflowException : Exception {
    /// <inheritdoc />
    public CounterOverflowException() { }

    /// <inheritdoc />
    public CounterOverflowException(string message) : base(message) { }

    /// <inheritdoc />
    public CounterOverflowException(string message, Exception innerException) : base(message, innerException) { }
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
