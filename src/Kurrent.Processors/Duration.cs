using System.ComponentModel;
using System.Globalization;

namespace Kurrent.Processors;

/// <summary>
/// A duration value that binds from milliseconds (long) in configuration
/// and supports <see cref="TimeSpan"/> in programmatic usage via implicit conversion.
/// </summary>
/// <example>
/// <code>
/// // From code — TimeSpan implicit conversion
/// Duration d = TimeSpan.FromSeconds(10);
///
/// // From code — static factory
/// var d = Duration.FromMilliseconds(30000);
/// var d = Duration.FromSeconds(10);
///
/// // From JSON config — milliseconds (standard across ecosystems)
/// // "Interval": 30000
///
/// // From JSON config — TimeSpan format (also supported)
/// // "Interval": "00:00:30"
/// </code>
/// </example>
[PublicAPI]
[TypeConverter(typeof(DurationConverter))]
public readonly record struct Duration(TimeSpan Value) : IComparable<Duration>, IComparable {
    public static readonly Duration Zero = new(TimeSpan.Zero);

    public static Duration FromMilliseconds(double ms) => new(TimeSpan.FromMilliseconds(ms));
    public static Duration FromSeconds(double seconds) => new(TimeSpan.FromSeconds(seconds));
    public static Duration FromMinutes(double minutes) => new(TimeSpan.FromMinutes(minutes));

    public double TotalMilliseconds => Value.TotalMilliseconds;
    public double TotalSeconds => Value.TotalSeconds;

    public static implicit operator TimeSpan(Duration d) => d.Value;
    public static implicit operator Duration(TimeSpan ts) => new(ts);

    public int CompareTo(Duration other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj) => obj is Duration other
        ? CompareTo(other)
        : throw new ArgumentException($"Object must be of type {nameof(Duration)}");

    public static bool operator >(Duration left, Duration right) => left.Value > right.Value;
    public static bool operator <(Duration left, Duration right) => left.Value < right.Value;
    public static bool operator >=(Duration left, Duration right) => left.Value >= right.Value;
    public static bool operator <=(Duration left, Duration right) => left.Value <= right.Value;

    public override string ToString() => Value.ToString();
}

/// <summary>
/// TypeConverter for <see cref="Duration"/> that supports:
/// <list type="bullet">
///   <item>Numeric strings → parsed as milliseconds (e.g., "30000" → 30 seconds)</item>
///   <item>TimeSpan format strings → parsed as TimeSpan (e.g., "00:00:30" → 30 seconds)</item>
/// </list>
/// This enables IConfiguration.Bind() to work with both formats in appsettings.json.
/// </summary>
sealed class DurationConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
        if (value is string s) {
            // Try as milliseconds first (standard across ecosystems)
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                return Duration.FromMilliseconds(ms);

            // Fall back to TimeSpan format ("00:00:30", "1.00:00:00", etc.)
            if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts))
                return new Duration(ts);

            throw new FormatException($"Cannot convert '{s}' to Duration. Use milliseconds (e.g., 30000) or TimeSpan format (e.g., 00:00:30).");
        }

        return base.ConvertFrom(context, culture, value)!;
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) {
        if (destinationType == typeof(string) && value is Duration d)
            return ((long)d.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}
