using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Registry;

public interface IRegistryCommands {
    ValueTask<SchemaVersionDescriptor> CreateSchema(CreateSchemaOptions options, CancellationToken ct = default);

    ValueTask<SchemaVersionDescriptor> CreateSchema(SchemaName schemaName, Action<CreateSchemaOptions>? configure = null, CancellationToken ct = default);

    ValueTask<Success> DeleteSchema(SchemaName schemaName, CancellationToken ct = default);

    ValueTask<SchemaVersionDescriptor> RegisterSchemaVersion(SchemaName schemaName, string schemaDefinition, CancellationToken ct = default);
}

public interface IRegistryQueries {
    ValueTask<Schema> GetSchema(SchemaName schemaName, CancellationToken ct = default);

    ValueTask<SchemaVersion> GetSchemaVersion(SchemaName schemaName, CancellationToken ct = default);

    ValueTask<SchemaVersion> GetSchemaVersion(SchemaName schemaName, int versionNumber, CancellationToken ct = default);

    ValueTask<SchemaVersion> GetSchemaVersionById(SchemaVersionId schemaVersionId, CancellationToken ct = default);

    ValueTask<SchemaVersionId> CheckSchemaCompatibility(CheckSchemaCompatibilityOptions options, CancellationToken ct = default);

    ValueTask<SchemaVersionId> CheckSchemaCompatibility(SchemaIdentifier identifier, Action<CheckSchemaCompatibilityOptions>? configure = null, CancellationToken ct = default);

    ValueTask<IReadOnlyList<Schema>> ListSchemas(ListSchemasOptions options, CancellationToken ct = default);

    ValueTask<IReadOnlyList<Schema>> ListSchemas(Action<ListSchemasOptions>? configure = null, CancellationToken ct = default);
}

public interface IRegistryClient : IRegistryCommands, IRegistryQueries;
