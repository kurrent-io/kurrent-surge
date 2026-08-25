using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kurrent.Surge.Schema;

/// <summary>
/// Converts a format's own schema into a JSON Schema document.
/// </summary>
/// <remarks>
/// The document describes the JSON the format's transcoder actually emits, not an idealised view of
/// the values. A schema that its own transcoder's output fails is not a stricter schema, it is a
/// wrong one — so a logical type is written as the base type the wire carries.
/// <para>
/// Draft 2020-12, always. The validator reads that draft and nothing older, so there is no dialect
/// to select.
/// </para>
/// </remarks>
[PublicAPI]
public interface IJsonSchemaExporter {
    JsonNode Export(SchemaSource schema, JsonSchemaExportOptions? options = null);
}

[PublicAPI]
public static class JsonSchemaExporterExtensions {
    extension(IJsonSchemaExporter exporter) {
        /// <summary>Exports the schema as indented JSON text.</summary>
        public string ExportAsString(SchemaSource schema, JsonSchemaExportOptions? options = null) {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                exporter.Export(schema, options).WriteTo(writer);

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
