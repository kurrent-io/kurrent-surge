using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Kurrent.Surge.Client;

/// <summary>
/// The text form of <see cref="ClientCredentials"/>, for configuration and connection strings.
/// </summary>
/// <remarks>
/// Only <c>none</c> and username/password have a text form. Certificate credentials need two paths,
/// so they are written as an object — <c>{ "Mode": "FileCertificate", "CertificatePath": "…" }</c> —
/// and read by <c>KurrentSurgeConfigurationReader</c>.
/// </remarks>
[TypeConverter(typeof(ClientCredentialsTypeConverter))]
public readonly partial record struct ClientCredentials : IParsable<ClientCredentials> {
    internal const string NoneText = "none";

    /// <summary>
    /// Parses <c>none</c>, or <c>username:password</c>.
    /// </summary>
    /// <param name="s">The credentials. Case-sensitive apart from the <c>none</c> keyword.</param>
    /// <param name="provider">Unused. Credentials are culture-invariant.</param>
    /// <exception cref="FormatException">The text is empty.</exception>
    public static ClientCredentials Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var value)
            ? value
            : throw new FormatException(
                $"'{s}' is not valid credentials. Use {NoneText} or username:password, " +
                "or an object with a Mode of FileCertificate.");

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        out ClientCredentials result
    ) {
        if (string.IsNullOrWhiteSpace(s)) {
            result = default;
            return false;
        }

        if (string.Equals(s.Trim(), NoneText, StringComparison.OrdinalIgnoreCase)) {
            result = None;
            return true;
        }

        // Split on the first separator only: a password may legitimately contain colons.
        var separator = s.IndexOf(':');

        var username = separator < 0 ? s : s[..separator];
        var password = separator < 0 ? null : s[(separator + 1)..];

        if (string.IsNullOrWhiteSpace(username)) {
            result = default;
            return false;
        }

        result = Basic(username, string.IsNullOrEmpty(password) ? null : password);
        return true;
    }

    /// <summary>
    /// Renders the credentials. The password is never included — this is used in diagnostics.
    /// </summary>
    public string ToText() =>
        Match(
            _     => NoneText,
            basic => $"{basic.Username}:***",
            _     => "filecertificate",
            _     => "x509certificate"
        );
}

/// <summary>
/// Bridges <see cref="ClientCredentials"/> to anything that resolves conversions through
/// <see cref="TypeDescriptor"/>.
/// </summary>
/// <remarks>
/// Converting back to text redacts the password, so this converter is not a round trip.
/// </remarks>
sealed class ClientCredentialsTypeConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text
            ? ClientCredentials.Parse(text, culture)
            : base.ConvertFrom(context, culture, value);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType
    ) =>
        destinationType == typeof(string) && value is ClientCredentials credentials
            ? credentials.ToText()
            : base.ConvertTo(context, culture, value, destinationType);
}
