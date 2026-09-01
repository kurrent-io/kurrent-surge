using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;

namespace Kurrent.Surge.Client;

/// <summary>
/// The text form of a KurrentDB endpoint: <c>host</c>, <c>host:port</c>, or <c>[::1]:port</c>.
/// </summary>
/// <remarks>
/// The port defaults to <see cref="DefaultPort"/> when omitted, so <c>node-a</c> and
/// <c>node-a:2113</c> mean the same thing.
/// </remarks>
static class EndPointSyntax {
    public const int DefaultPort = 2113;

    public static bool TryParse(string? text, out DnsEndPoint value) {
        value = null!;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.Trim();

        // A bracketed IPv6 literal carries colons of its own, so the port separator is the one
        // after the closing bracket — not the last colon in the string.
        var separator = span.StartsWith('[')
            ? span.IndexOf(':', span.IndexOf(']') + 1)
            : span.LastIndexOf(':');

        var host = separator < 0 ? span : span[..separator];
        var port = DefaultPort;

        if (separator >= 0 &&
            !int.TryParse(span[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            return false;

        if (host.Length == 0 || port is < 1 or > 65535)
            return false;

        value = new DnsEndPoint(host, port);
        return true;
    }

    public static string ToText(DnsEndPoint value) =>
        $"{value.Host}:{value.Port.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Bridges <see cref="DnsEndPoint"/> to anything that resolves conversions through
/// <see cref="TypeDescriptor"/>.
/// </summary>
/// <remarks>
/// <see cref="DnsEndPoint"/> belongs to the BCL, so the converter cannot be attached with an
/// attribute and is registered at module load instead — the same way the SDK attaches its duration
/// converter to <see cref="TimeSpan"/>.
/// </remarks>
sealed class DnsEndPointTypeConverter : TypeConverter {
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
        if (value is not string text)
            return base.ConvertFrom(context, culture, value);

        if (EndPointSyntax.TryParse(text, out var endpoint))
            return endpoint;

        throw new FormatException($"Cannot convert '{text}' to an endpoint. Use host or host:port.");
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType
    ) =>
        destinationType == typeof(string) && value is DnsEndPoint endpoint
            ? EndPointSyntax.ToText(endpoint)
            : base.ConvertTo(context, culture, value, destinationType);
}

static class DnsEndPointTypeConverterRegistration {
    // Library-level module initializer, matching ConfigTimeSpanConverter: DnsEndPoint has no
    // converter of its own, so attaching one adds behaviour without changing any existing.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() =>
        TypeDescriptor.AddAttributes(typeof(DnsEndPoint), new TypeConverterAttribute(typeof(DnsEndPointTypeConverter)));
}
