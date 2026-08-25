using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

/// <summary>
/// Thrown when a JSON Schema document cannot be expressed as a <c>.proto</c> schema.
/// </summary>
/// <remarks>
/// Carries the JSON Pointer of the offending node. A contradiction — an <c>x-protobuf</c> key that
/// disagrees with the standard keyword beside it — throws rather than resolving in favour of either,
/// because a silent preference decodes payloads wrongly instead of failing.
/// </remarks>
[PublicAPI]
public sealed class ProtobufJsonSchemaException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("PROTOBUF_JSON_SCHEMA_INVALID");

    public ProtobufJsonSchemaException(string pointer, string message) : base(Code, $"{pointer}: {message}") =>
        Pointer = pointer;

    /// <summary>The JSON Pointer of the node that could not be converted.</summary>
    public string Pointer { get; }
}
