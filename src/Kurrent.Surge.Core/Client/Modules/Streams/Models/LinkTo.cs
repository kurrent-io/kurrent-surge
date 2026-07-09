using System.Text;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Represents the target of a link event, indicating which stream and revision the link points to.
/// </summary>
[PublicAPI]
public readonly record struct LinkTo(StreamName Stream, Revision Revision)
    : IUtf8SpanParsable<LinkTo>, ISpanParsable<LinkTo> {

    public static readonly SchemaName SchemaName = "$>";

    public static readonly LinkTo Unset = new(StreamName.Unset, Revision.Unset);

    public bool IsUnset => this == Unset;

    /// <summary>
    /// Parses a UTF-8 link event data in the format "{revision}@{stream}".
    /// </summary>
    public static LinkTo Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider = null) {
        var separatorIndex = utf8Text.IndexOf((byte)'@');
        if (separatorIndex < 0)
            throw new FormatException("Invalid link data format. Expected '{revision}@{stream}'.");

        var revision = long.Parse(utf8Text[..separatorIndex], provider: provider);
        var stream   = Encoding.UTF8.GetString(utf8Text[(separatorIndex + 1)..]);

        return new(StreamName.From(stream), Revision.From(revision));
    }

    /// <summary>
    /// Tries to parse a UTF-8 link event data in the format "{revision}@{stream}".
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out LinkTo result) {
        result = Unset;

        var separatorIndex = utf8Text.IndexOf((byte)'@');
        if (separatorIndex < 0)
            return false;

        if (!long.TryParse(utf8Text[..separatorIndex], provider: provider, out var revision))
            return false;

        var stream = Encoding.UTF8.GetString(utf8Text[(separatorIndex + 1)..]);
        result = new(StreamName.From(stream), Revision.From(revision));
        return true;
    }

    /// <summary>
    /// Parses a char span link event data in the format "{revision}@{stream}".
    /// </summary>
    public static LinkTo Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) {
        var separatorIndex = s.IndexOf('@');
        if (separatorIndex < 0)
            throw new FormatException("Invalid link data format. Expected '{revision}@{stream}'.");

        var revision = long.Parse(s[..separatorIndex], provider: provider);
        var stream   = s[(separatorIndex + 1)..];

        return new(StreamName.From(stream.ToString()), Revision.From(revision));
    }

    /// <summary>
    /// Tries to parse a char span link event data in the format "{revision}@{stream}".
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LinkTo result) {
        result = Unset;

        var separatorIndex = s.IndexOf('@');
        if (separatorIndex < 0)
            return false;

        if (!long.TryParse(s[..separatorIndex], provider: provider, out var revision))
            return false;

        var stream = s[(separatorIndex + 1)..];
        result = new(StreamName.From(stream.ToString()), Revision.From(revision));
        return true;
    }

    // ISpanParsable<T> requires string overloads
    public static LinkTo Parse(string s, IFormatProvider? provider = null) => Parse(s.AsSpan(), provider);
    public static bool TryParse(string? s, IFormatProvider? provider, out LinkTo result) =>
        s is not null ? TryParse(s.AsSpan(), provider, out result) : (result = Unset) == Unset && false;
}
