using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Kurrent.Surge.Client;

/// <summary>
/// The text form of <see cref="TransportSecurity"/>, for configuration and connection strings.
/// </summary>
/// <remarks>
/// Only the three modes that carry no extra data have a text form. A certificate mode needs a path,
/// so it is written as an object — <c>{ "Mode": "FileCertificate", "CaPath": "…" }</c> — and read by
/// <c>KurrentSurgeConfigurationReader</c>.
/// </remarks>
[TypeConverter(typeof(TransportSecurityTypeConverter))]
public readonly partial record struct TransportSecurity : IParsable<TransportSecurity> {
    internal const string NoneText     = "none";
    internal const string StandardText = "standard";
    internal const string InsecureText = "insecure";

    /// <summary>
    /// Parses <c>none</c>, <c>standard</c> (or <c>tls</c>), or <c>insecure</c>.
    /// </summary>
    /// <param name="s">The mode name. Case-insensitive.</param>
    /// <param name="provider">Unused. Mode names are culture-invariant.</param>
    /// <exception cref="FormatException">The text names no mode.</exception>
    public static TransportSecurity Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var value)
            ? value
            : throw new FormatException(
                $"'{s}' is not a transport security mode. Use {NoneText}, {StandardText} or {InsecureText}, " +
                "or an object with a Mode of FileCertificate.");

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out TransportSecurity result
    ) {
        switch (s?.Trim().ToLowerInvariant()) {
            case NoneText:
                result = None;
                return true;

            // "tls" is what a connection string calls it; both spellings reach the same mode.
            case StandardText or "tls":
                result = Standard;
                return true;

            case InsecureText:
                result = Insecure;
                return true;

            default:
                result = default;
                return false;
        }
    }

    /// <summary>
    /// Renders the mode name. Certificate modes carry a path, so their text form names the mode
    /// only and does not round-trip through <see cref="Parse"/>.
    /// </summary>
    public string ToText() =>
        Match(
            _   => NoneText,
            std => std.VerifyServerCertificate ? StandardText : InsecureText,
            _   => "filecertificate",
            _   => "x509certificate"
        );
}

/// <summary>
/// Bridges <see cref="TransportSecurity"/> to anything that resolves conversions through
/// <see cref="TypeDescriptor"/>.
/// </summary>
/// <remarks>
/// Reached by the reflection configuration binder, ASP.NET model binding and designers. The SDK's
/// own reader calls <see cref="TransportSecurity.Parse"/> directly, since
/// <c>TypeDescriptor.GetConverter</c> breaks trimming.
/// </remarks>
sealed class TransportSecurityTypeConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text
            ? TransportSecurity.Parse(text, culture)
            : base.ConvertFrom(context, culture, value);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType
    ) =>
        destinationType == typeof(string) && value is TransportSecurity security
            ? security.ToText()
            : base.ConvertTo(context, culture, value, destinationType);
}
