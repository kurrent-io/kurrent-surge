using Kurrent.Client.Streams;

namespace Kurrent.Client.Registry;

public static partial class RegistryClientExtensions {
    extension(RegistryClient client) {
        #region ->> CreateSchema <<-

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken ct = default
        ) => client.CreateSchema(
            CreateSchemaRequest.For(schemaName)
                .WithDefinition(schemaDefinition)
                .WithDataFormat(dataFormat)
                .Build(),
            ct);

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CompatibilityMode compatibilityMode, CancellationToken ct = default
        ) => client.CreateSchema(
            CreateSchemaRequest.For(schemaName)
                .WithDefinition(schemaDefinition)
                .WithDataFormat(dataFormat)
                .WithCompatibility(compatibilityMode)
                .Build(),
            ct);

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.CreateSchema(
            CreateSchemaRequest.For(schemaName)
                .WithDefinition(schemaDefinition)
                .WithDataFormat(dataFormat)
                .WithTags(tags)
                .Build(),
            ct);

        #endregion

        #region ->> CheckSchemaCompatibility <<-

        public ValueTask<SchemaVersionId> CheckSchemaCompatibility(
            SchemaIdentifier identifier, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken ct = default
        ) => client.CheckSchemaCompatibility(
            CheckSchemaCompatibilityRequest.For(identifier)
                .WithDefinition(schemaDefinition)
                .WithDataFormat(dataFormat)
                .Build(),
            ct);

        #endregion

        #region ->> ListSchemas <<-

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(CancellationToken ct = default) =>
            client.ListSchemas(ListSchemasRequest.New.Build(), ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(string schemaNamePrefix, CancellationToken ct = default) =>
            client.ListSchemas(ListSchemasRequest.New.WithPrefix(schemaNamePrefix).Build(), ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(
            IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.ListSchemas(ListSchemasRequest.New.WithTags(tags).Build(), ct);

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(
            string schemaNamePrefix, IReadOnlyDictionary<string, string> tags, CancellationToken ct = default
        ) => client.ListSchemas(
            ListSchemasRequest.New.WithPrefix(schemaNamePrefix).WithTags(tags).Build(),
            ct);

        #endregion
    }
}
