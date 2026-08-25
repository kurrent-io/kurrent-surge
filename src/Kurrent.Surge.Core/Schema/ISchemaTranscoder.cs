using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kurrent.Surge.Schema;

/// <summary>
/// Rewrites a payload as JSON, guided by the schema that wrote it.
/// </summary>
/// <remarks>
/// The schema is resolved from text on each call, so an implementation caches that work against the
/// <see cref="SchemaSource"/> it was handed. Holding one source and reusing it takes the cached path;
/// building a fresh source per record pays the parse every time.
/// </remarks>
[PublicAPI]
public interface ISchemaTranscoder {
    /// <summary>Writes <paramref name="payload"/> into <paramref name="writer"/> as JSON.</summary>
    /// <remarks>
    /// The primitive, because the string and node forms are both derivable from it and it is the only
    /// one that can write into a caller's own buffer. The writer is not flushed — the caller owns it.
    /// </remarks>
    /// <exception cref="InvalidDataException">The payload does not match the schema.</exception>
    void ToJson(SchemaSource schema, ReadOnlyMemory<byte> payload, Utf8JsonWriter writer);
}

[PublicAPI]
public static class SchemaTranscoderExtensions {
    extension(ISchemaTranscoder transcoder) {
        /// <summary>Rewrites <paramref name="payload"/> as compact JSON text.</summary>
        public string ToJson(SchemaSource schema, ReadOnlyMemory<byte> payload) {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer)) transcoder.ToJson(schema, payload, writer);

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>Rewrites <paramref name="payload"/> as a <see cref="JsonNode"/>.</summary>
        /// <remarks>The overload to use when the result is inspected or embedded rather than written out.</remarks>
        public JsonNode ToJsonNode(SchemaSource schema, ReadOnlyMemory<byte> payload) =>
            JsonNode.Parse(transcoder.ToJson(schema, payload))!;
    }
}
