using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Kurrent.Surge.Codecs.Avro.Generators.Model;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;

namespace Kurrent.Surge.Codecs.Avro;

public static partial class AvroJsonSchema {
    #region ->> Assertions <<-

    /// <summary>Matches the Avro JSON encoding of <c>bytes</c> and <c>fixed</c>.</summary>
    /// <remarks>
    /// The specification writes each byte as the Unicode code point of the same value, so a byte string is
    /// exactly the characters U+0000 to U+00FF and its length is the byte count. It is not base64, and
    /// reading it as base64 is the bug this codec already shipped once.
    /// <para>
    /// <c>pattern</c> is a draft 4 keyword and an assertion in every draft, unlike <c>contentEncoding</c>,
    /// which the specification states is not an assertion at all.
    /// </para>
    /// </remarks>
    internal const string ByteStringPattern = @"^[\x00-\xFF]*$";

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
    internal static string? LogicalOf(AvroJsonValue? raw, AvroSchema baseSchema) {
        return raw?.TextOf("logicalType") is { } logical && MatchesBase(logical, baseSchema) ? logical : null;

        /// <remarks>
        /// A logical type on a base the specification does not allow it on is erased to the base type, which
        /// is both what the specification requires and what the parser already does.
        /// </remarks>
        static bool MatchesBase(string logicalType, AvroSchema baseSchema) => logicalType switch {
            "decimal"  => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.Bytes } or AvroFixed,
            "uuid"     => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.String } or AvroFixed { Size: 16 },
            "duration" => baseSchema is AvroFixed { Size: 12 },

            "date" or "time-millis" => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.Int },

            "time-micros" or "timestamp-millis" or "timestamp-micros" or "timestamp-nanos"
                or "local-timestamp-millis" or "local-timestamp-micros" or "local-timestamp-nanos"
                => baseSchema is AvroPrimitive { Kind: AvroPrimitiveKind.Long },

            _ => true
        };
    }

    #endregion // Logical types

    #region ->> JSON interop <<-

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
