using Kurrent.Surge.Errors;

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
    public static Errors.ErrorCode Code { get; } = Errors.ErrorCode.From("AVRO_CODEC_NOT_GENERATED");

    public AvroCodecNotGeneratedException(Type messageType) : this(
        $"No generated Avro codec for '{messageType.FullName}'. Add its .avsc schema to the project as "
      + "<AdditionalFiles Include=\"Schemas/*.avsc\" /> so the codec is generated at compile time.") =>
        MessageType = messageType;

    public AvroCodecNotGeneratedException(string message) : base(Code, message) { }

    public Type? MessageType { get; init; }
}
