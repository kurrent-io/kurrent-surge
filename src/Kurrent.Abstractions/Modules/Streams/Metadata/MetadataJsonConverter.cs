using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kurrent.Client.Registry;
using Kurrent.Schema;
using KurrentDB.Diagnostics.Tracing;

namespace Kurrent.Client.Streams;

// The contract-layer JSON converter for Metadata: Metadata is decorated [JsonConverter(typeof(MetadataJsonConverter))],
// so the converter must live alongside it in Kurrent.Abstractions. It is pure System.Text.Json —
// no gRPC, no client serialization layer — keyed off contract constants (SystemMetadataKeys, the
// schema value types, and the W3C trace-context keys).
class MetadataJsonConverter : JsonConverter<Metadata> {
    public override Metadata Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var metadata = new Metadata();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
            if (reader.GetString() is not { } propertyName || string.IsNullOrWhiteSpace(propertyName))
                throw new JsonException("Property name cannot be empty or whitespace");

            reader.Read();

            var value = reader.TokenType switch {
                JsonTokenType.None   => null,
                JsonTokenType.Null   => null,
                JsonTokenType.True   => true,
                JsonTokenType.False  => false,
                JsonTokenType.String => ParseString(reader, propertyName),

                // ValueSpan is invalid for segmented input — large payloads can arrive as a sequence.
                JsonTokenType.Number => reader.HasValueSequence ? Encoding.UTF8.GetString(reader.ValueSequence) : Encoding.UTF8.GetString(reader.ValueSpan),

                // Nested JSON (older clients write arbitrary metadata) is preserved as a JsonNode —
                // document-model parsing, no reflection, AOT-safe. Write round-trips it via WriteTo.
                JsonTokenType.StartObject or JsonTokenType.StartArray => JsonNode.Parse(ref reader),

                _ => throw new JsonException($"Unsupported metadata value type ({reader.TokenType}) for property '{propertyName}'")
            };

            metadata.With(propertyName, value);
        }

        return metadata;

        static object? ParseString(Utf8JsonReader reader, string propertyName) {
            if (propertyName.Equals(SystemMetadataKeys.SchemaName, StringComparison.OrdinalIgnoreCase)) {
                var value = reader.GetString();
                return string.IsNullOrWhiteSpace(value) ? SchemaName.Unset : SchemaName.From(value);
            }

            if (propertyName.Equals(SystemMetadataKeys.SchemaFormat, StringComparison.OrdinalIgnoreCase))
                return Enum.TryParse<SchemaDataFormat>(reader.GetString(), ignoreCase: true, out var format)
                    ? format : SchemaDataFormat.Unspecified;

            if (propertyName.Equals(SystemMetadataKeys.SchemaId, StringComparison.OrdinalIgnoreCase))
                return reader.TryGetGuid(out var versionId) && versionId != Guid.Empty
                    ? SchemaVersionId.From(versionId) : SchemaVersionId.Unset;

            // W3C trace IDs are 32/16-char hex strings — valid base64 alphabet at %4 lengths,
            // so the probe below would reinterpret them as bytes and silently kill distributed
            // tracing on the consume side. Key-driven exemption keeps them strings. The same
            // literals are public as TraceContextKeys in Kurrent.Streaming — keep in lockstep.
            if (propertyName.Equals(TracingConstants.Metadata.TraceId, StringComparison.OrdinalIgnoreCase) ||
                propertyName.Equals(TracingConstants.Metadata.SpanId, StringComparison.OrdinalIgnoreCase))
                return reader.GetString();

            if (reader.TryGetGuid(out var guid)) return guid;
            if (reader.TryGetDateTime(out var dateTime)) return dateTime;
            if (reader.TryGetTimeSpan(out var timeSpan)) return timeSpan;

            // No base64 probe: any %4-length alphanumeric string is valid base64 ("abcd1234",
            // "Blue", W3C trace IDs), so shape-sniffing reinterprets innocent user strings as
            // bytes — actual data corruption. Binary values stay base64 strings in storage and
            // are recovered through the typed retrieval path: TryGetValue<byte[]/Memory>.
            return reader.GetString();
        }
    }

    public override void Write(Utf8JsonWriter writer, Metadata value, JsonSerializerOptions options) {
        writer.WriteStartObject();

        foreach (var (key, item) in value) {
            writer.WritePropertyName(key);
            WriteValue(writer, key, item);
        }

        writer.WriteEndObject();

        // The exact inverse of ParseString: every shape written here must decode back to the same
        // data through the Read path. The metadata value universe is closed by the decoder contract,
        // so this is a type-pattern switch instead of polymorphic serialization — zero reflection,
        // AOT-safe. Anything outside the contract throws loudly rather than silently morphing.
        static void WriteValue(Utf8JsonWriter writer, string key, object? value) {
            switch (value) {
                case null:               writer.WriteNullValue(); break;
                case bool b:             writer.WriteBooleanValue(b); break;
                case string s:           writer.WriteStringValue(s); break;
                case Guid g:             writer.WriteStringValue(g); break;
                case DateTime dt:        writer.WriteStringValue(dt); break;
                case TimeSpan ts:        writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture)); break;

                // Reads back as a DateTime of the same instant: the wire format cannot distinguish
                // a DateTimeOffset from a zoned DateTime, and the Read path tries DateTime first.
                case DateTimeOffset dto: writer.WriteStringValue(dto); break;

                // All binary shapes converge on base64; the Read path returns ReadOnlyMemory<byte>.
                case byte[] bytes:           writer.WriteBase64StringValue(bytes); break;
                case Memory<byte> mem:       writer.WriteBase64StringValue(mem.Span); break;
                case ReadOnlyMemory<byte> m: writer.WriteBase64StringValue(m.Span); break;

                // System schema values: ParseString re-types these from their well-known keys.
                case SchemaName sn:       writer.WriteStringValue(sn.Value); break;
                case SchemaVersionId id:  writer.WriteStringValue(id.Value); break;

                // Enums (including SchemaDataFormat) write as their names: the $schema.format system
                // key re-types on read, and user enums recover via TryGetValue<TEnum>'s parse path.
                case Enum e: writer.WriteStringValue(e.ToString()); break;

                // JSON has no NaN/Infinity tokens, and a string fallback would silently change
                // the stored type — reject with a clear error instead of letting the writer
                // throw a cryptic ArgumentException.
                case double d when !double.IsFinite(d):
                case float f when !float.IsFinite(f):
                    throw new JsonException(
                        $"Metadata value for property '{key}' is {value} — JSON cannot represent " +
                        "non-finite floating-point values. Store it as a string explicitly if needed.");

                // Numbers are written as JSON numbers; the Read path deliberately surfaces them as
                // invariant strings (decoder contract) and TryGetValue<T> converts back losslessly.
                case int n:     writer.WriteNumberValue(n); break;
                case long n:    writer.WriteNumberValue(n); break;
                case short n:   writer.WriteNumberValue(n); break;
                case byte n:    writer.WriteNumberValue(n); break;
                case sbyte n:   writer.WriteNumberValue(n); break;
                case uint n:    writer.WriteNumberValue(n); break;
                case ulong n:   writer.WriteNumberValue(n); break;
                case ushort n:  writer.WriteNumberValue(n); break;
                case float n:   writer.WriteNumberValue(n); break;
                case double n:  writer.WriteNumberValue(n); break;
                case decimal n: writer.WriteNumberValue(n); break;

                case char c: writer.WriteStringValue(c.ToString()); break;

                // JSON nodes (user-supplied values or nested JSON preserved by Read) write
                // themselves — symmetric with Read's JsonNode parsing, AOT-safe.
                case JsonNode node: node.WriteTo(writer); break;

                default:
                    throw new JsonException(
                        $"Unsupported metadata value type '{value.GetType()}' for property '{key}'. " +
                        "Metadata values are limited to booleans, strings, numbers, Guids, DateTimes, " +
                        "TimeSpans, and byte arrays. Convert the value before storing it.");
            }
        }
    }
}
