using Kurrent.Client.Streams;

namespace Kurrent.Client.Registry;

public static partial class RegistryClientIdiomaticExtensions {
    extension(RegistryClient client) {
        #region CreateSchema

        public async ValueTask<SchemaVersionDescriptor> CreateSchemaAsync(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CompatibilityMode compatibilityMode = CompatibilityMode.None,
            string description = "",
            IReadOnlyDictionary<string, string>? tags = null,
            CancellationToken cancellationToken = default
        ) => await client.CreateSchema(schemaName, schemaDefinition, dataFormat,
            compatibilityMode, description, tags, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region GetSchema

        public async ValueTask<Schema> GetSchemaAsync(
            SchemaName schemaName, CancellationToken cancellationToken = default
        ) => await client.GetSchema(schemaName, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region GetSchemaVersion

        public async ValueTask<SchemaVersion> GetSchemaVersionAsync(
            SchemaName schemaName, int? versionNumber = null,
            CancellationToken cancellationToken = default
        ) => await client.GetSchemaVersion(schemaName, versionNumber, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<SchemaVersion> GetSchemaVersionAsync(
            SchemaName schemaName, CancellationToken cancellationToken = default
        ) => await client.GetSchemaVersion(schemaName, null, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region GetSchemaVersionById

        public async ValueTask<SchemaVersion> GetSchemaVersionByIdAsync(
            SchemaVersionId schemaVersionId, CancellationToken cancellationToken = default
        ) => await client.GetSchemaVersionById(schemaVersionId, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region DeleteSchema

        public async ValueTask<Success> DeleteSchemaAsync(
            SchemaName schemaName, CancellationToken cancellationToken = default
        ) => await client.DeleteSchema(schemaName, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region CheckSchemaCompatibility

        public async ValueTask<SchemaVersionId> CheckSchemaCompatibilityAsync(
            SchemaIdentifier identifier, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken cancellationToken = default
        ) => await client.CheckSchemaCompatibility(identifier, schemaDefinition, dataFormat, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region ListSchemas

        public async ValueTask<IReadOnlyList<Schema>> ListSchemasAsync(
            string? schemaNamePrefix = null,
            IReadOnlyDictionary<string, string>? tags = null,
            CancellationToken cancellationToken = default
        ) => await client.ListSchemas(schemaNamePrefix, tags, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        public async ValueTask<IReadOnlyList<Schema>> ListSchemasAsync(
            CancellationToken cancellationToken
        ) => await client.ListSchemas(null, null, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion

        #region RegisterSchemaVersion

        public async ValueTask<SchemaVersionDescriptor> RegisterSchemaVersionAsync(
            SchemaName schemaName, string schemaDefinition,
            CancellationToken cancellationToken = default
        ) => await client.RegisterSchemaVersion(schemaName, schemaDefinition, cancellationToken)
            .UnwrapAsync().ConfigureAwait(false);

        #endregion
    }
}
