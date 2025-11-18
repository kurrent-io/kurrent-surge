using Kurrent.Client.Registry;

namespace Kurrent.Client.Streams;

[PublicAPI]
public static class MetadataSchemaExtensions {
	extension(Metadata metadata) {
        public RecordSchemaInfo GetSchemaInfo() =>
            new(GetSchemaName(metadata), GetSchemaDataFormat(metadata), GetSchemaVersionId(metadata));

        public SchemaName GetSchemaName() =>
            metadata.GetOrDefault(SystemMetadataKeys.SchemaName, SchemaName.None);

        public SchemaDataFormat GetSchemaDataFormat() =>
            metadata.GetOrDefault(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Unspecified);

        public SchemaVersionId GetSchemaVersionId() =>
            metadata.GetOrDefault(SystemMetadataKeys.SchemaId, SchemaVersionId.None);

        public Metadata AddSchemaNameIfMissing(SchemaName schemaName) =>
            metadata.WithIf(x => x.GetSchemaName() == SchemaName.None, SystemMetadataKeys.SchemaName, schemaName);

        public Metadata AddSchemaDataFormatIfMissing(SchemaDataFormat dataFormat) {
            return dataFormat != SchemaDataFormat.Unspecified
                ? metadata.WithIf(x => x.GetSchemaDataFormat() == SchemaDataFormat.Unspecified, SystemMetadataKeys.SchemaFormat, dataFormat)
                : throw new ArgumentOutOfRangeException(nameof(dataFormat), "SchemaDataFormat cannot be Unspecified when adding to metadata.");
        }
    }
}
