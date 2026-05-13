namespace Kurrent.Client.Streams;

/// <summary>
/// Represents the target of a link event, indicating which stream and revision the link points to.
/// </summary>
[PublicAPI]
public readonly record struct LinkTo(StreamName Stream, Revision Revision) {
    public static readonly LinkTo Unset = new(StreamName.Unset, Revision.Unset);

    public bool IsUnset => this == Unset;

    /// <summary>
    /// Parses a link event data string in the format "{revision}@{stream}".
    /// </summary>
    public static LinkTo Parse(ReadOnlySpan<char> data) {
        var separatorIndex = data.IndexOf('@');
        if (separatorIndex < 0)
            throw new FormatException($"Invalid link data format: '{data}'. Expected '{{revision}}@{{stream}}'.");

        var revision = long.Parse(data[..separatorIndex]);
        var stream   = data[(separatorIndex + 1)..];

        return new(StreamName.From(stream.ToString()), Revision.From(revision));
    }
}
