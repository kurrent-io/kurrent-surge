using Kurrent.Client.Streams;

namespace Kurrent.Client.Registry;

public static partial class RegistryClientExtensions {
    static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    extension(RegistryClient client) {
        #region ->> CreateSchema <<-

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken ct = default
        ) => client.CreateSchema(schemaName, schemaDefinition, dataFormat, CompatibilityMode.None, "", EmptyTags, ct);

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CompatibilityMode compatibilityMode, CancellationToken ct = default
        ) => client.CreateSchema(schemaName, schemaDefinition, dataFormat, compatibilityMode, "", EmptyTags, ct);

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.CreateSchema(schemaName, schemaDefinition, dataFormat, CompatibilityMode.None, "", tags, ct);

        #endregion

        #region ->> ListSchemas <<-

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(CancellationToken ct = default) =>
            client.ListSchemas(null, null, ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(string schemaNamePrefix, CancellationToken ct = default) =>
            client.ListSchemas(schemaNamePrefix, null, ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(
            IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.ListSchemas(null, tags, ct);

        #endregion
    }
}
