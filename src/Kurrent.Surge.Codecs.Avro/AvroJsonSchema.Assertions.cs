using System.Text.Json.Nodes;
using AvroJsonKind = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonKind;
using AvroJsonValue = Kurrent.Surge.Codecs.Avro.Generators.Json.JsonValue;

namespace Kurrent.Surge.Codecs.Avro;

public sealed partial class AvroJsonSchema {
    #region ->> Assertions <<-

    /// <summary>Matches the Avro JSON encoding of <c>bytes</c> and <c>fixed</c>.</summary>
    /// <remarks>
    /// The specification writes each byte as the Unicode code point of the same value, so a byte string is
    /// exactly the characters U+0000 to U+00FF and its length is the byte count. It is not base64, and
    /// reading it as base64 is the bug this codec already shipped once.
    /// <para>
    /// <c>pattern</c> is an assertion in every draft, unlike <c>contentEncoding</c>, which the
    /// specification states is not an assertion at all.
    /// </para>
    /// </remarks>
    internal const string ByteStringPattern = @"^[\x00-\xFF]*$";

    #endregion // Assertions

    #region ->> JSON interop <<-

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
