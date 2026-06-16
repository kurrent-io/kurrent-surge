using Kurrent.Client.Registry;
using Kurrent.Schema;

namespace Kurrent.Client.Streams;

/// <summary>
/// Represents information about the schema of a record, including its name, format, and version identifier.
/// </summary>
public record RecordSchemaInfo {
    public static readonly RecordSchemaInfo None = new();

    /// <summary>
    /// The name of the schema for the record.
    /// </summary>
    public SchemaName Schema { get; init; } = SchemaName.Unset;

    /// <summary>
    /// The data format of the schema, such as JSON, Protobuf, Avro, Bytes, or Unspecified.
    /// </summary>
    public SchemaDataFormat DataFormat { get; init; } = SchemaDataFormat.Unspecified;

    /// <summary>
    /// The unique identifier for the version of the schema.
    /// </summary>
    public SchemaVersionId VersionId { get; init; } = SchemaVersionId.Unset;

    public bool HasSchema     => Schema     != SchemaName.Unset;
    public bool HasDataFormat => DataFormat != SchemaDataFormat.Unspecified;
    public bool HasSchemaId   => VersionId  != SchemaVersionId.Unset;

    public override string ToString() => $"{Schema} {DataFormat} {VersionId}";
}
