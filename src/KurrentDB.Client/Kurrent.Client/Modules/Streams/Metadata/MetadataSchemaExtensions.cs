using Kurrent.Client.Registry;

namespace Kurrent.Client.Streams;

[PublicAPI]
public static class MetadataSchemaExtensions {
	extension(Metadata source) {
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
                SchemaName      = source.GetSchemaName(),
                SchemaFormat    = source.GetSchemaFormat(),
                SchemaVersionId = source.GetSchemaId()
            };

            source.Remove(SystemMetadataKeys.SchemaName);
            source.Remove(SystemMetadataKeys.SchemaFormat);
            source.Remove(SystemMetadataKeys.SchemaId);

            return info;
        }

        public SchemaName GetSchemaName() =>
            source.GetOrDefault(SystemMetadataKeys.SchemaName, SchemaName.None);

        public SchemaDataFormat GetSchemaFormat() =>
            source.GetOrDefault(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Unspecified);

        public SchemaVersionId GetSchemaId() =>
            source.GetOrDefault(SystemMetadataKeys.SchemaId, SchemaVersionId.None);

        public Metadata AddSchemaNameIfMissing(SchemaName schemaName) =>
            source.WithIf(x => x.GetSchemaName() == SchemaName.None, SystemMetadataKeys.SchemaName, schemaName);

        public Metadata AddSchemaFormatIfMissing(SchemaDataFormat dataFormat) {
            return dataFormat != SchemaDataFormat.Unspecified
                ? source.WithIf(x => x.GetSchemaFormat() == SchemaDataFormat.Unspecified, SystemMetadataKeys.SchemaFormat, dataFormat)
                : throw new ArgumentOutOfRangeException(nameof(dataFormat), "SchemaFormat cannot be Unspecified when adding to metadata.");
        }
    }
}
