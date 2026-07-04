using Kurrent.Client.Streams;

namespace Kurrent.Client.Schema.Serialization;

/// <summary>
/// Provides context information for schema serialization and deserialization operations.
/// </summary>
public class SchemaSerializationContext {
    /// <summary>
    /// The name of the data stream being processed.
    /// </summary>
    public string Stream { get; set; } = "";

    /// <summary>
    /// Information about the record's schema.
    /// </summary>
    public RecordSchemaInfo RecordSchema { get; set; } = RecordSchemaInfo.None;

    /// <summary>
    /// Policy settings for schema registry interactions.
    /// </summary>
    public SchemaRegistryPolicy SchemaRegistryPolicy { get; set; } = SchemaRegistryPolicy.NoRequirements;

    /// <summary>
    /// Token for cancellation support during async operations.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
