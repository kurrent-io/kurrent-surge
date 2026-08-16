using Kurrent.Surge.Codecs;

namespace Kurrent.Surge.Client.Schema.Serialization.Avro;

/// <summary>
/// Options for <see cref="AvroSchemaCodec"/>.
/// </summary>
[PublicAPI]
public record AvroSchemaCodecOptions : SchemaCodecOptions {
    public static AvroSchemaCodecOptions Default => new();
}
