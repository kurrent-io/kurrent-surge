using System.Text;
using Google.Protobuf.Reflection;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

public static partial class ProtobufJsonSchema {
    #region ->> Assertions <<-

    /// <summary>Matches the base64 form the mapping writes a <c>bytes</c> field as.</summary>
    /// <remarks>
    /// Standard base64 with padding, which is what <c>Base64.EncodeToUtf8</c> produces. An empty field
    /// writes an empty string, so the pattern accepts one.
    /// <para>
    /// <c>pattern</c> is a draft 4 keyword and an assertion in every draft, unlike
    /// <c>contentEncoding</c>, which the specification states is not an assertion at all.
    /// </para>
    /// </remarks>
    internal const string Base64Pattern = "^[A-Za-z0-9+/]*={0,2}$";

    /// <summary>Matches the decimal string a signed 64-bit field is written as.</summary>
    /// <remarks>
    /// The mapping spells 64-bit integers as JSON strings, because a JSON number cannot hold the full
    /// range without loss. See https://protobuf.dev/programming-guides/json/.
    /// </remarks>
    internal const string SignedDigitsPattern = "^-?[0-9]+$";

    /// <inheritdoc cref="SignedDigitsPattern"/>
    internal const string UnsignedDigitsPattern = "^[0-9]+$";

    /// <summary>The three values a JSON number cannot carry, which the mapping spells as strings.</summary>
    internal static readonly string[] NonFiniteValues = ["NaN", "Infinity", "-Infinity"];

    #endregion // Assertions

    #region ->> Type discrimination <<-

    /// <summary>
    /// The proto type a node declares, or null when the standard keywords already pin it.
    /// </summary>
    /// <remarks>
    /// Several proto types share one JSON form: three signed 32-bit encodings all write a number in the
    /// int32 range, and five 64-bit encodings all write a decimal string. One of each group is the
    /// documented default that <c>Import</c> infers, and only the others are named — stating the
    /// default would state it twice.
    /// </remarks>
    internal static string? DiscriminatorOf(FieldDescriptor field) => field.FieldType switch {
        Type.SInt32 => "sint32",
        Type.SFixed32 => "sfixed32",
        Type.Fixed32 => "fixed32",

        Type.SInt64 => "sint64",
        Type.SFixed64 => "sfixed64",
        Type.Fixed64 => "fixed64",

        // JSON Schema has one numeric type, so double is the default and only float is marked.
        Type.Float => "float",

        // A group is a message declaration and a field at once. Nothing in the JSON form says which,
        // and Import has to write `group Foo = 1 {…}` rather than a message plus a field.
        Type.Group => "group",

        _ => null
    };

    #endregion // Type discrimination

    #region ->> Presence <<-

    /// <summary>How a field tracks presence, or null when the standard keywords carry it.</summary>
    /// <remarks>
    /// <c>required</c> is binary and protobuf presence is not. The keyword cannot be read backwards
    /// either: under <c>EmitDefaults</c> an implicit-presence field is always written and so lands in
    /// <c>required</c> beside a proto2 <c>required</c> one, and the two mean different things.
    /// <para>
    /// A repeated field is named by <c>type: array</c>, so it needs nothing here.
    /// </para>
    /// </remarks>
    internal static string? PresenceOf(FieldDescriptor field) =>
        field.IsRepeated ? null
        : field.IsRequired ? "required"
        : field.HasPresence ? "explicit"
        : "implicit";

    #endregion // Presence

    #region ->> Field names <<-

    /// <summary>The JSON name the mapping derives from a declared field name.</summary>
    /// <remarks>
    /// Strip the underscores and upper-case whatever followed each one. Read backwards it is not a
    /// function — <c>order_id</c>, <c>orderId</c> and <c>orderID</c> do not all come back — and
    /// <c>json_name</c> can override the result outright, so a name that does not match this is carried
    /// rather than derived. See https://protobuf.dev/programming-guides/json/.
    /// </remarks>
    internal static string DeriveJsonName(string protoName) {
        var name       = new StringBuilder(protoName.Length);
        var capitalise = false;

        foreach (var character in protoName) {
            if (character == '_') {
                capitalise = true;
                continue;
            }

            name.Append(capitalise ? char.ToUpperInvariant(character) : character);

            capitalise = false;
        }

        return name.ToString();
    }

    #endregion // Field names
}
