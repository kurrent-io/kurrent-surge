using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Thrown when a message type has no source-generated Avro codec.
/// </summary>
/// <remarks>
/// The codec has no reflection-based fallback, deliberately: the Apache.Avro path it would fall back to
/// cannot run under Native AOT. A type reaches this exception when its schema was never supplied as an
/// <c>AdditionalFiles</c> <c>.avsc</c> entry, so the generator never emitted a codec for it.
/// </remarks>
[PublicAPI]
public sealed class AvroCodecNotGeneratedException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("AVRO_CODEC_NOT_GENERATED");

    public AvroCodecNotGeneratedException(Type messageType) : this(
        $"No generated Avro codec for '{messageType.FullName}'. Add its .avsc schema to the project as "
      + "<AdditionalFiles Include=\"Schemas/*.avsc\" /> so the codec is generated at compile time.") =>
        MessageType = messageType;

    public AvroCodecNotGeneratedException(string message) : base(Code, message) { }

    public Type? MessageType { get; init; }
}

/// <summary>
/// Thrown when a JSON Schema document cannot be expressed as an Avro schema.
/// </summary>
/// <remarks>
/// Carries the JSON Pointer of the offending node. A contradiction — an <c>x-avro</c> key that disagrees
/// with the standard keyword beside it — throws rather than resolving in favour of either, because a
/// silent preference decodes payloads wrongly instead of failing.
/// </remarks>
[PublicAPI]
public sealed class AvroJsonSchemaException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("AVRO_JSON_SCHEMA_INVALID");

    public AvroJsonSchemaException(string pointer, string message) : base(Code, $"{pointer}: {message}") =>
        Pointer = pointer;

    /// <summary>The JSON Pointer of the node that could not be converted.</summary>
    public string Pointer { get; }
}
