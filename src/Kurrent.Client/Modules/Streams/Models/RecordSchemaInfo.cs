using Kurrent.Client.Registry;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents information about the schema of a record, including its name, format, and version identifier.
/// </summary>
public record RecordSchemaInfo {
    public static readonly RecordSchemaInfo None = new();

    /// <summary>
    /// The name of the schema for the record.
    /// </summary>
    public SchemaName SchemaName { get; init; } = SchemaName.Unset;

    /// <summary>
    /// The data format of the schema, such as JSON, Protobuf, Avro, Bytes, or Unspecified.
    /// </summary>
    public SchemaDataFormat SchemaFormat { get; init; } = SchemaDataFormat.Unspecified;

    /// <summary>
    /// The unique identifier for the version of the schema.
    /// </summary>
    public SchemaVersionId SchemaVersionId { get; init; } = SchemaVersionId.Unset;

    public bool HasSchemaName   => SchemaName != SchemaName.Unset;
    public bool HasSchemaFormat => SchemaFormat != SchemaDataFormat.Unspecified;
    public bool HasSchemaId     => SchemaVersionId != SchemaVersionId.Unset;

    public override string ToString() => $"{SchemaName} {SchemaFormat} {SchemaVersionId}";
}
