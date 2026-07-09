using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Streams;

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
                Schema      = source.GetSchemaName(),
                DataFormat    = source.GetSchemaFormat(),
                VersionId = source.GetSchemaId()
            };

            source.Remove(SystemMetadataKeys.SchemaName);
            source.Remove(SystemMetadataKeys.SchemaFormat);
            source.Remove(SystemMetadataKeys.SchemaId);

            return info;
        }

        public SchemaName GetSchemaName() =>
            source.GetValueOrDefault(SystemMetadataKeys.SchemaName, SchemaName.Unset);

        public SchemaDataFormat GetSchemaFormat() =>
            source.GetValueOrDefault(SystemMetadataKeys.SchemaFormat, SchemaDataFormat.Unspecified);

        public SchemaVersionId GetSchemaId() =>
            source.GetValueOrDefault(SystemMetadataKeys.SchemaId, SchemaVersionId.Unset);

        public Metadata AddSchemaNameIfMissing(SchemaName schemaName) =>
            source.WithIf(SystemMetadataKeys.SchemaName, schemaName, source.GetSchemaName() == SchemaName.Unset);

        public Metadata AddSchemaFormatIfMissing(SchemaDataFormat dataFormat) {
            return dataFormat != SchemaDataFormat.Unspecified
                ? source.WithIf(SystemMetadataKeys.SchemaFormat, dataFormat, source.GetSchemaFormat() == SchemaDataFormat.Unspecified)
                : throw new ArgumentOutOfRangeException(nameof(dataFormat), "SchemaFormat cannot be Unspecified when adding to metadata.");
        }
    }
}
