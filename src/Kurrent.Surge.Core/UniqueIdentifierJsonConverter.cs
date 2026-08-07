using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kurrent.Surge;

/// <summary>
/// AOT-safe <see cref="System.Text.Json"/> converter for any <see cref="UniqueIdentifierAttribute"/>-generated
/// type. One generic converter serves every adopter through the surface the generator guarantees —
/// no per-type converter needed.
/// </summary>
public sealed class UniqueIdentifierJsonConverter<T> : JsonConverter<T>
    where T : struct, IUniqueIdentifier, ISpanParsable<T>, IUtf8SpanFormattable {
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected a JSON string for {typeToConvert}.");

        // 64 chars comfortably covers both faces the generated type accepts: the fixed 26-char
        // sortable form and any GUID string format (longest is 38 chars, braced).
        Span<char> buffer = stackalloc char[64];
        var        length = reader.CopyString(buffer);

        return T.Parse(buffer[..length], provider: null);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) {
        // Every [UniqueIdentifier]-generated type shares the same generated TryFormat, which
        // always writes exactly 26 bytes for the sortable form on success.
        Span<byte> buffer = stackalloc byte[26];
        value.TryFormat(buffer, out var bytesWritten, format: default, provider: null);

        writer.WriteStringValue(buffer[..bytesWritten]);
    }
}
