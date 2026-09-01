using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// TypeConverter attached to <see cref="TimeSpan"/> via <see cref="TypeDescriptor.AddAttributes"/>
/// at module init. Extends the default TimeSpan conversion with numeric milliseconds support so
/// <c>IConfiguration</c> can bind a plain number like <c>"Interval": 30000</c> directly into a
/// <see cref="TimeSpan"/> property.
/// <list type="bullet">
///   <item>"30000" → 30 seconds (integer milliseconds)</item>
///   <item>"00:00:30" → 30 seconds (standard TimeSpan format)</item>
///   <item>"1.00:00:00" → 1 day (standard TimeSpan format)</item>
/// </list>
/// Strict superset of the default <see cref="TimeSpan"/> converter: every value the default
/// converter accepts still round-trips unchanged.
/// </summary>
sealed class ConfigTimeSpanConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
        if (value is not string text)
            return base.ConvertFrom(context, culture, value)!;

        if (DurationSyntax.TryParse(text, out var duration))
            return duration;

        throw new FormatException(
            $"Cannot convert '{text}' to TimeSpan. Use milliseconds (e.g., 30000) or TimeSpan format (e.g., 00:00:30).");
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) {
        if (destinationType == typeof(string) && value is TimeSpan duration)
            return DurationSyntax.ToText(duration);

        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}

static class ConfigTimeSpanConverterRegistration {
    // Intentional library-level module initializer: we own the converter and the registration
    // is a strict superset of the default TimeSpan behavior, so it cannot break host apps.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() =>
        TypeDescriptor.AddAttributes(typeof(TimeSpan), new TypeConverterAttribute(typeof(ConfigTimeSpanConverter)));
}
