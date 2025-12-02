using Kurrent.Client.Registry;

namespace Kurrent.Client.Streams;

[PublicAPI]
public static class MetadataSchemaExtensions {
	extension(Metadata metadata) {
        // public RecordSchemaInfo GetSchemaInfo() =>
        //     new RecordSchemaInfo {
        //         SchemaName      = metadata.GetSchemaName(),
        //         SchemaFormat    = metadata.GetSchemaFormat(),
        //         SchemaVersionId = metadata.GetSchemaId()
        //     };

        /// <summary>
        /// Extracts schema information from the metadata and removes the related entries.
        /// </summary>
        public RecordSchemaInfo ExtractSchemaInfo() {
            var info = new RecordSchemaInfo {
                SchemaName      = metadata.GetSchemaName(),
                SchemaFormat    = metadata.GetSchemaFormat(),
                SchemaVersionId = metadata.GetSchemaId()
            };

            metadata.Remove(SystemMetadataKeys.SchemaName);
            metadata.Remove(SystemMetadataKeys.SchemaFormat);
            metadata.Remove(SystemMetadataKeys.SchemaId);

            return info;
        }

        public SchemaName GetSchemaName() =>
            metadata.GetOrDefault(SystemMetadataKeys.SchemaName, SchemaName.None);

        public SchemaDataFormat GetSchemaFormat() =>
            metadata.GetOrDefault(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Unspecified);

        public SchemaVersionId GetSchemaId() =>
            metadata.GetOrDefault(SystemMetadataKeys.SchemaId, SchemaVersionId.None);

        public Metadata AddSchemaNameIfMissing(SchemaName schemaName) =>
            metadata.WithIf(x => x.GetSchemaName() == SchemaName.None, SystemMetadataKeys.SchemaName, schemaName);

        public Metadata AddSchemaFormatIfMissing(SchemaDataFormat dataFormat) {
            return dataFormat != SchemaDataFormat.Unspecified
                ? metadata.WithIf(x => x.GetSchemaFormat() == SchemaDataFormat.Unspecified, SystemMetadataKeys.SchemaFormat, dataFormat)
                : throw new ArgumentOutOfRangeException(nameof(dataFormat), "SchemaFormat cannot be Unspecified when adding to metadata.");
        }
    }
}
