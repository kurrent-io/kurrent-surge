using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;

namespace Kurrent.Surge.Codecs.Avro;

public static partial class AvroJsonSchema {
    #region ->> Assertions <<-

    // In 2020-12 `format` is annotation-only and MUST be disabled by default, and `contentEncoding` is
    // explicitly not an assertion — "a malformed string-encoded document MUST NOT cause the containing
    // instance to be considered invalid". Every annotation below is therefore paired with a pattern or a
    // length, or the exported schema would validate nothing.
    // https://json-schema.org/draft/2020-12/json-schema-validation#section-7.2.1

    internal const string Base64Pattern = "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$";

    internal const string UuidPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    internal const string DatePattern = @"^\d{4}-\d{2}-\d{2}$";

    internal const string TimePattern = @"^\d{2}:\d{2}:\d{2}(\.\d+)?$";

    // One pattern for the absolute and the local variants alike. They share `format: date-time` and the
    // annotation is what separates them, so a stricter local pattern would make x-avro.logicalType
    // recoverable for the wrong reason.
    internal const string DateTimePattern = @"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})?$";

    internal const string DurationPattern = @"^P(\d+Y)?(\d+M)?(\d+W)?(\d+D)?(T(\d+H)?(\d+M)?(\d+(\.\d+)?S)?)?$";

    /// <summary>Base64 encodes <paramref name="bytes"/> bytes to this many characters.</summary>
    /// <remarks>
    /// Three-to-one at every size, so it is not invertible anywhere: 16, 17 and 18 bytes all encode to 24
    /// characters. This length asserts the encoded size; only <c>x-avro.size</c> recovers the byte count.
    /// </remarks>
    internal static int Base64Length(int bytes) => 4 * ((bytes + 2) / 3);

    internal static string DecimalPattern(int? precision, int scale) {
        var digits   = precision is { } p && p > scale ? $"{{1,{p - scale}}}" : "+";
        var fraction = scale > 0 ? $@"(\.\d{{{scale}}})?" : "";

        return $@"^-?\d{digits}{fraction}$";
    }

    #endregion // Assertions

    #region ->> Recognised Avro attributes <<-

    // Anything outside these sets is a custom attribute. The Avro specification permits them and requires
    // them preserved, so the remainder of each object goes to x-avro.props.
    // https://avro.apache.org/docs/1.12.0/specification/#schema-declaration

    internal static readonly FrozenSet<string> RecordKeys    = new[] { "type", "name", "namespace", "doc", "aliases", "fields" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenSet<string> FieldKeys     = new[] { "name", "type", "doc", "default", "order", "aliases" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenSet<string> EnumKeys      = new[] { "type", "name", "namespace", "doc", "aliases", "symbols", "default" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenSet<string> FixedKeys     = new[] { "type", "name", "namespace", "doc", "aliases", "size", "logicalType", "precision", "scale" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenSet<string> ArrayKeys     = new[] { "type", "items" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenSet<string> MapKeys       = new[] { "type", "values" }.ToFrozenSet(StringComparer.Ordinal);
    internal static readonly FrozenSet<string> PrimitiveKeys = new[] { "type", "logicalType", "precision", "scale" }.ToFrozenSet(StringComparer.Ordinal);

    #endregion // Recognised Avro attributes

    #region ->> Logical types <<-

    /// <summary>The logical type declared on <paramref name="raw"/>, or null when there is none to carry.</summary>
    /// <remarks>
    /// Read from the schema text rather than from <c>AvroPrimitive.Logical</c>. The parser models only the
    /// logical types the code generator emits and erases the rest, and the specification says an
    /// unrecognised logical type must be ignored rather than dropped.
    /// </remarks>
    internal static string? LogicalOf(AvroJsonValue? raw, AvroSchema baseSchema) =>
        raw?.TextOf("logicalType") is { } logical && MatchesBase(logical, baseSchema) ? logical : null;

    /// <remarks>
    /// A logical type on a base the specification does not allow it on is erased to the base type, which
    /// is both what the specification requires and what the parser already does.
    /// </remarks>
    static bool MatchesBase(string logicalType, AvroSchema baseSchema) => logicalType switch {
        "decimal" => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.Bytes } or AvroFixed,
        "uuid"    => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.String } or AvroFixed { Size: 16 },
        "duration" => baseSchema is AvroFixed { Size: 12 },

        "date" or "time-millis" => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.Int },

        "time-micros" or "timestamp-millis" or "timestamp-micros" or "timestamp-nanos"
            or "local-timestamp-millis" or "local-timestamp-micros" or "local-timestamp-nanos"
            => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.Long },

        _ => true
    };

    #endregion // Logical types

    #region ->> JSON interop <<-

    /// <summary>Rewrites an Avro byte-string as base64.</summary>
    /// <remarks>
    /// Avro writes a <c>bytes</c> or <c>fixed</c> default as one Unicode code point per byte, so
    /// <c>0xFF</c> is U+00FF. It is not base64, and reading it as base64 is the bug this codec already
    /// shipped once. Every byte maps to exactly one code point, so the conversion is total both ways.
    /// </remarks>
    internal static string ToBase64(string byteString) {
        var bytes = new byte[byteString.Length];

        for (var i = 0; i < byteString.Length; i++) bytes[i] = (byte)byteString[i];

        return Convert.ToBase64String(bytes);
    }

    /// <summary>Rewrites base64 as the Avro byte-string that produced it.</summary>
    internal static string ToByteString(string base64) {
        var bytes = Convert.FromBase64String(base64);

        return string.Create(bytes.Length, bytes, static (span, source) => {
            for (var i = 0; i < source.Length; i++) span[i] = (char)source[i];
        });
    }

    internal static int? Integer(AvroJsonValue? raw, string name) =>
        raw?[name] is { Kind: AvroJsonKind.Number } number ? (int)number.Number : null;

    internal static JsonNode? ToJsonNode(AvroJsonValue value) => value.Kind switch {
        AvroJsonKind.Object => new JsonObject(value.Members
            .OrderBy(member => member.Key, StringComparer.Ordinal)
            .Select(member => KeyValuePair.Create(member.Key, ToJsonNode(member.Value)))),

        AvroJsonKind.Array   => new JsonArray(value.Items.Select(ToJsonNode).ToArray()),
        AvroJsonKind.String  => JsonValue.Create(value.Text),
        AvroJsonKind.Number  => JsonValue.Create(value.Number),
        AvroJsonKind.Boolean => JsonValue.Create(value.Boolean),

        _ => null
    };

    #endregion // JSON interop
}
