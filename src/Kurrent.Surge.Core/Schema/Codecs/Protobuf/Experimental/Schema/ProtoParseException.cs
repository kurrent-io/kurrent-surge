namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Thrown when <c>.proto</c> text cannot be parsed.
/// </summary>
[PublicAPI]
public sealed class ProtoParseException(string file, int line, int column, string message)
    : Exception($"{file}:{line}:{column}: {message}") {
    /// <summary>The name the schema was registered under.</summary>
    public string File { get; } = file;

    /// <summary>The one-based line the parser stopped on.</summary>
    public int Line { get; } = line;

    /// <summary>The one-based column the parser stopped on.</summary>
    public int Column { get; } = column;
}
