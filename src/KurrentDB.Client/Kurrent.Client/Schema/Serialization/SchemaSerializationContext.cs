using Kurrent.Client.Streams;

namespace Kurrent.Client.Schema.Serialization;

/// <summary>
/// Provides context information for schema serialization and deserialization operations.
/// </summary>
/// <param name="Stream">The name of the data stream being processed.</param>
/// <param name="Metadata">Additional metadata associated with the operation.</param>
/// <param name="SchemaRegistryPolicy">Policy settings for schema registry interactions.</param>
/// <param name="CancellationToken">Token for cancellation support during async operations.</param>
/// <param name="RecordSchema">Information about the record's schema.</param>s
public record struct SchemaSerializationContext(
    string Stream,
    Metadata Metadata,
    SchemaRegistryPolicy SchemaRegistryPolicy,
    CancellationToken CancellationToken,
    RecordSchemaInfo RecordSchema);
