namespace ProtobufTranscoder;

/// <summary>
/// Thrown when a .proto file contains syntax errors.
/// </summary>
public sealed class ProtoParseException : Exception
{
    public string File { get; }
    public int Line { get; }
    public int Column { get; }

    public ProtoParseException(string file, int line, int column, string message)
        : base($"{file}:{line}:{column}: {message}")
    {
        File = file;
        Line = line;
        Column = column;
    }
}
