using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Registry;

public static partial class RegistryClientExtensions {
    extension(IRegistryClient client) {
        #region ->> CreateSchema <<-

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken ct = default
        ) => client.CreateSchema(
            schemaName,
            r => {
                r.SchemaDefinition = schemaDefinition;
                r.DataFormat       = dataFormat;
            },
            ct);

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CompatibilityMode compatibilityMode, CancellationToken ct = default
        ) => client.CreateSchema(
            schemaName,
            r => {
                r.SchemaDefinition  = schemaDefinition;
                r.DataFormat        = dataFormat;
                r.CompatibilityMode = compatibilityMode;
            },
            ct);

        #endregion

        #region ->> CheckSchemaCompatibility <<-

        public ValueTask<SchemaVersionId> CheckSchemaCompatibility(
            SchemaIdentifier identifier, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken ct = default
        ) => client.CheckSchemaCompatibility(
            identifier,
            r => {
                r.SchemaDefinition = schemaDefinition;
                r.DataFormat       = dataFormat;
            },
            ct);

        #endregion

        #region ->> ListSchemas <<-

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(CancellationToken ct = default) =>
            client.ListSchemas(new ListSchemasOptions(), ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(string schemaNamePrefix, CancellationToken ct = default) =>
            client.ListSchemas(r => r.SchemaNamePrefix = schemaNamePrefix, ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(
            IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.ListSchemas(r => r.Tags = new Dictionary<string, string>(tags), ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(
            string schemaNamePrefix, IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.ListSchemas(
            r => {
                r.SchemaNamePrefix = schemaNamePrefix;
                r.Tags             = new Dictionary<string, string>(tags);
            },
            ct);

        #endregion
    }
}
