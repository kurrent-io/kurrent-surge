namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Options for <see cref="AvroSchemaCodec"/>.
/// </summary>
[PublicAPI]
public sealed class AvroSchemaCodecOptions : SchemaCodecOptions {
    public static AvroSchemaCodecOptions Default => new();
}
