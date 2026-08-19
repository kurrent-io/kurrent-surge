namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Options for <see cref="AvroSchemaCodec"/>.
/// </summary>
[PublicAPI]
public record AvroSchemaCodecOptions : SchemaCodecOptions {
    public static AvroSchemaCodecOptions Default => new();
}
