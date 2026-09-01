namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// An instant on the global timeline, to nanosecond precision.
/// </summary>
/// <remarks>
/// The CLR type for the <c>timestamp-nanos</c> logical type. <see cref="DateTime"/> cannot carry it:
/// its resolution is 100 nanoseconds, so every value would be silently truncated.
/// <para>
/// The range is the Avro format's, not this type's. <c>timestamp-nanos</c> is encoded as a signed
/// 64-bit count of nanoseconds from the epoch, which spans 1677-09-21 to 2262-04-11. No representation
/// reaches further, so values outside it cannot be expressed as <c>timestamp-nanos</c> at all.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct NanoInstant(long TotalNanoseconds) : IComparable<NanoInstant> {
    const long NanosecondsPerSecond = 1_000_000_000;

    /// <summary>The earliest instant expressible as <c>timestamp-nanos</c>: 1677-09-21T00:12:43.145224192Z.</summary>
    public static NanoInstant MinValue => new(long.MinValue);

    /// <summary>The latest instant expressible as <c>timestamp-nanos</c>: 2262-04-11T23:47:16.854775807Z.</summary>
    public static NanoInstant MaxValue => new(long.MaxValue);

    public static NanoInstant UnixEpoch => new(0);

    /// <param name="epochSeconds">Whole seconds from the epoch, negative before it.</param>
    /// <param name="nanoOfSecond">Nanoseconds within the second. Values outside 0..999,999,999 carry.</param>
    public NanoInstant(long epochSeconds, int nanoOfSecond)
        : this(checked(epochSeconds * NanosecondsPerSecond + nanoOfSecond)) { }

    /// <summary>Whole seconds from the epoch, rounded towards negative infinity.</summary>
    public long EpochSeconds => FloorDivide(TotalNanoseconds, NanosecondsPerSecond);

    /// <summary>Nanoseconds within the second, always 0..999,999,999 including before the epoch.</summary>
    public int NanoOfSecond {
        get {
            var remainder = (int)(TotalNanoseconds % NanosecondsPerSecond);

            return remainder < 0 ? remainder + (int)NanosecondsPerSecond : remainder;
        }
    }

    /// <summary>Converts to <see cref="DateTimeOffset"/>, truncating to its 100-nanosecond resolution.</summary>
    public DateTimeOffset ToDateTimeOffset() =>
        DateTimeOffset.UnixEpoch.AddTicks(FloorDivide(TotalNanoseconds, TimeSpan.NanosecondsPerTick));

    public static NanoInstant FromDateTimeOffset(DateTimeOffset value) =>
        new((value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * TimeSpan.NanosecondsPerTick);

    public int CompareTo(NanoInstant other) => TotalNanoseconds.CompareTo(other.TotalNanoseconds);

    public static bool operator <(NanoInstant left, NanoInstant right)  => left.CompareTo(right) < 0;
    public static bool operator >(NanoInstant left, NanoInstant right)  => left.CompareTo(right) > 0;
    public static bool operator <=(NanoInstant left, NanoInstant right) => left.CompareTo(right) <= 0;
    public static bool operator >=(NanoInstant left, NanoInstant right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        $"{ToDateTimeOffset():yyyy-MM-ddTHH:mm:ss}.{NanoOfSecond:D9}Z";

    // C# division truncates towards zero, which would put a pre-epoch instant in the wrong second and
    // give it a negative nano-of-second.
    static long FloorDivide(long value, long divisor) {
        var quotient = value / divisor;

        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
