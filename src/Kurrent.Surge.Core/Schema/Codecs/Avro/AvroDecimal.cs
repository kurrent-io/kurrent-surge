using System.Globalization;
using System.Numerics;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// An arbitrary-precision decimal: an unscaled integer and the power of ten it is divided by.
/// </summary>
/// <remarks>
/// This is the shape the Avro <c>decimal</c> logical type stores — the specification defines it as a
/// two's-complement unscaled value plus a scale from the schema. <see cref="decimal"/> cannot stand in
/// for it: Avro places no bound on precision, and <see cref="decimal"/> stops at 96 bits.
/// <para>
/// Equality is representational, not numeric. <c>1.50</c> and <c>1.5</c> hold different unscaled values
/// and encode to different bytes, so they are not equal here. Within one schema the scale is fixed, so
/// values that came off the same wire always compare as expected. <see cref="CompareTo"/> aligns the
/// scales first and is numeric.
/// </para>
/// </remarks>
[PublicAPI]
public readonly record struct AvroDecimal : IComparable<AvroDecimal>, IComparable, IFormattable {
    /// <param name="unscaledValue">The value, as an integer, before the scale is applied.</param>
    /// <param name="scale">The power of ten <paramref name="unscaledValue"/> is divided by.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is negative.</exception>
    public AvroDecimal(BigInteger unscaledValue, int scale) {
        if (scale < 0)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "An Avro decimal scale cannot be negative");

        UnscaledValue = unscaledValue;
        Scale         = scale;
    }

    public AvroDecimal(decimal value) {
        Span<int> bits = stackalloc int[4];

        decimal.GetBits(value, bits);

        // Layout is documented on decimal.GetBits: three 32-bit words of magnitude, then a flags word
        // carrying the scale in bits 16-23 and the sign in bit 31.
        var magnitude = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0];

        Scale         = (bits[3] >> 16) & 0xFF;
        UnscaledValue = bits[3] < 0 ? -magnitude : magnitude;
    }

    /// <summary>The value as an integer, before the scale is applied.</summary>
    public BigInteger UnscaledValue { get; }

    /// <summary>The power of ten <see cref="UnscaledValue"/> is divided by.</summary>
    public int Scale { get; }

    public static AvroDecimal Zero => new(BigInteger.Zero, 0);

    public int Sign => UnscaledValue.Sign;

    /// <summary>
    /// Converts to <see cref="decimal"/>.
    /// </summary>
    /// <exception cref="OverflowException">The value does not fit in a <see cref="decimal"/>.</exception>
    public decimal ToDecimal() {
        if (Scale > 28)
            throw new OverflowException($"A scale of {Scale} exceeds the 28 digits a decimal can hold");

        // Checked so a value wider than 96 bits throws rather than wrapping. BigInteger's explicit
        // conversion is already checked; the cast is spelled out to say so.
        var unscaled = (decimal)UnscaledValue;

        return unscaled / Pow10(Scale);
    }

    /// <remarks>
    /// Scales are aligned with <see cref="BigInteger"/> arithmetic rather than by narrowing to
    /// <see cref="decimal"/>, so a value wider than 96 bits compares correctly instead of overflowing.
    /// </remarks>
    public int CompareTo(AvroDecimal other) {
        if (Scale == other.Scale) return UnscaledValue.CompareTo(other.UnscaledValue);

        var left  = UnscaledValue;
        var right = other.UnscaledValue;

        if (Scale < other.Scale)
            left *= BigInteger.Pow(10, other.Scale - Scale);
        else
            right *= BigInteger.Pow(10, Scale - other.Scale);

        return left.CompareTo(right);
    }

    public int CompareTo(object? obj) =>
        obj switch {
            null              => 1,
            AvroDecimal other => CompareTo(other),
            _                 => throw new ArgumentException($"Cannot compare an {nameof(AvroDecimal)} to {obj.GetType()}", nameof(obj))
        };

    public static bool operator <(AvroDecimal left, AvroDecimal right)  => left.CompareTo(right) < 0;
    public static bool operator >(AvroDecimal left, AvroDecimal right)  => left.CompareTo(right) > 0;
    public static bool operator <=(AvroDecimal left, AvroDecimal right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AvroDecimal left, AvroDecimal right) => left.CompareTo(right) >= 0;

    public static explicit operator decimal(AvroDecimal value) => value.ToDecimal();

    public static implicit operator AvroDecimal(decimal value) => new(value);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <remarks>
    /// Rendered from the unscaled digits rather than through <see cref="decimal"/>, so a value too wide
    /// for <see cref="decimal"/> still prints exactly.
    /// </remarks>
    public string ToString(string? format, IFormatProvider? formatProvider) {
        var digits = BigInteger.Abs(UnscaledValue).ToString(CultureInfo.InvariantCulture);
        var sign    = UnscaledValue.Sign < 0 ? "-" : "";

        if (Scale == 0) return sign + digits;

        if (digits.Length <= Scale) digits = digits.PadLeft(Scale + 1, '0');

        var point = digits.Length - Scale;

        return $"{sign}{digits[..point]}.{digits[point..]}";
    }

    static decimal Pow10(int exponent) {
        var result = 1m;

        for (var index = 0; index < exponent; index++) result *= 10m;

        return result;
    }
}
