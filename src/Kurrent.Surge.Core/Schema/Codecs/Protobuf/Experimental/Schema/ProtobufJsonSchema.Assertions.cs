namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

public sealed partial class ProtobufJsonSchema {
    /// <summary>Matches the base64 form the mapping writes a <c>bytes</c> field as.</summary>
    /// <remarks>
    /// Standard base64 with padding, which is what <c>Base64.EncodeToUtf8</c> produces. An empty field
    /// writes an empty string, so the pattern accepts one.
    /// <para>
    /// <c>pattern</c> is an assertion in every draft, unlike <c>contentEncoding</c>, which the
    /// specification states is not an assertion at all.
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
}
