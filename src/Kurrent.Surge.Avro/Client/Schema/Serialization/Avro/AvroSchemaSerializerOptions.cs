namespace Kurrent.Surge.Client.Schema.Serialization.Avro;

/// <summary>
/// Options for <see cref="AvroSchemaSerializer"/>.
/// </summary>
[PublicAPI]
public record AvroSchemaSerializerOptions : SchemaSerializerOptions {
    public static AvroSchemaSerializerOptions Default => new();
}
