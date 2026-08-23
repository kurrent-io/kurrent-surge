namespace ProtobufTranscoder;

/// <summary>
/// Thrown when protobuf wire format data cannot be decoded.
/// </summary>
public sealed class DecodeException : Exception
{
    public DecodeException(string message) : base(message) { }
    public DecodeException(string message, Exception innerException) : base(message, innerException) { }
}
