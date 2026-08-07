using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization;

/// <summary>
/// The subset of <c>SchemaManager</c>'s contract that <see cref="SchemaSerializerBase"/> depends
/// on. Exists so serializer construction can be declared in Kurrent.Surge.Core, which has no gRPC
/// client or transport — the concrete implementation (backed by the schema registry) lives in
/// Kurrent.Surge.
/// </summary>
public interface ISchemaManager {
    ValueTask<(SchemaName SchemaName, SchemaVersionId SchemaVersionId)> RegisterOrValidateSchema(
        Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct);

    ValueTask<(Type? MessageType, SchemaVersionId SchemaVersionId)> ValidateAndEnsureSchemaCompatibility(
        RecordSchemaInfo schemaInfo, ResolvedSchemaRegistryPolicy policy, CancellationToken ct);
}
