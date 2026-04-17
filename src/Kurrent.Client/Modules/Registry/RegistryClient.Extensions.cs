using Kurrent.Client.Streams;

namespace Kurrent.Client.Registry;

public static partial class RegistryClientExtensions {
    extension(RegistryClient client) {
        #region ->> CreateSchema <<-

        public ValueTask<SchemaVersionDescriptor> CreateSchema(
            SchemaName schemaName, string schemaDefinition, SchemaDataFormat dataFormat,
            CancellationToken cancellationToken
        ) => client.CreateSchema(schemaName, schemaDefinition, dataFormat, cancellationToken: cancellationToken);

        #endregion

        #region ->> GetSchemaVersion <<-

        public ValueTask<SchemaVersion> GetSchemaVersion(
            SchemaName schemaName, CancellationToken cancellationToken = default
        ) => client.GetSchemaVersion(schemaName, null, cancellationToken);

        #endregion

        #region ->> ListSchemas <<-

        public ValueTask<IReadOnlyList<Schema>> ListSchemas(
            CancellationToken cancellationToken
        ) => client.ListSchemas(null, null, cancellationToken);

        #endregion
    }
}
