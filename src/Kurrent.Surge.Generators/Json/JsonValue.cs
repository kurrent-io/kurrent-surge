using System.Globalization;
using System.Text;

namespace Kurrent.Surge.Codecs.Avro.Generators.Json;

enum JsonKind { Object, Array, String, Number, Boolean, Null }

sealed class JsonValue {
    public JsonKind Kind { get; private set; }

    public string                       Text    { get; private set; } = "";
    public double                       Number  { get; private set; }
    public bool                         Boolean { get; private set; }
    public List<JsonValue>              Items   { get; private set; } = [];
    public Dictionary<string, JsonValue> Members { get; private set; } = [];

    /// <summary>Offset of the first character of this value in the source text.</summary>
    public int Start { get; internal set; }

    /// <summary>Offset one past the last character of this value in the source text.</summary>
    public int End { get; internal set; }

    public JsonValue? this[string name] => Members.TryGetValue(name, out var value) ? value : null;

    public string? TextOf(string name) => this[name] is { Kind: JsonKind.String } v ? v.Text : null;

    public static JsonValue FromObject(Dictionary<string, JsonValue> members) =>
        new() { Kind = JsonKind.Object, Members = members };

    public static JsonValue FromArray(List<JsonValue> items) => new() { Kind = JsonKind.Array, Items = items };
    public static JsonValue FromString(string text)          => new() { Kind = JsonKind.String, Text = text };
    public static JsonValue FromNumber(double number)        => new() { Kind = JsonKind.Number, Number = number };
    public static JsonValue FromBoolean(bool value)          => new() { Kind = JsonKind.Boolean, Boolean = value };
    public static JsonValue FromNull()                       => new() { Kind = JsonKind.Null };
}

/// <summary>
/// Minimal recursive-descent JSON reader for .avsc files.
/// </summary>
/// <remarks>
/// Hand-rolled rather than System.Text.Json: this assembly is a Roslyn analyzer, and analyzers cannot
/// reliably load sibling dependency assemblies in every host (the reason Kurrent.CodeAnalysis is
/// source-baked rather than referenced). Avro schema documents are small and use no JSON feature beyond
/// the base grammar, so the parser stays self-contained.
/// </remarks>
static class JsonParser {
    public static bool TryParse(string text, out JsonValue value, out string error, out int offset) {
        var position = 0;

        try {
            value = ParseValue(text, ref position);
            SkipWhitespace(text, ref position);

            if (position != text.Length) throw new JsonParseException("unexpected trailing content", position);

            error  = "";
            offset = 0;
            return true;
        }
        catch (JsonParseException exception) {
            value  = JsonValue.FromNull();
            error  = exception.Message;
            offset = exception.Offset;
            return false;
        }
    }

    static JsonValue ParseValue(string text, ref int position) {
        SkipWhitespace(text, ref position);

        if (position >= text.Length) throw new JsonParseException("unexpected end of input", position);

        var start = position;

        var value = text[position] switch {
            '{' => ParseObject(text, ref position),
            '[' => ParseArray(text, ref position),
            '"' => JsonValue.FromString(ParseString(text, ref position)),
            't' => ParseLiteral(text, ref position, "true", JsonValue.FromBoolean(true)),
            'f' => ParseLiteral(text, ref position, "false", JsonValue.FromBoolean(false)),
            'n' => ParseLiteral(text, ref position, "null", JsonValue.FromNull()),
            _   => JsonValue.FromNumber(ParseNumber(text, ref position))
        };

        // The span is kept so a named type's declaration can be re-emitted verbatim as its Avro schema
        // text, rather than reconstructed from the model and risking a drift from the file on disk.
        value.Start = start;
        value.End   = position;

        return value;
    }

    static JsonValue ParseObject(string text, ref int position) {
        position++; // '{'
        var members = new Dictionary<string, JsonValue>();

        SkipWhitespace(text, ref position);

        if (Peek(text, position) == '}') {
            position++;
            return JsonValue.FromObject(members);
        }

        while (true) {
            SkipWhitespace(text, ref position);

            if (Peek(text, position) != '"') throw new JsonParseException("expected a property name", position);

            var name = ParseString(text, ref position);

            SkipWhitespace(text, ref position);

            if (Peek(text, position) != ':') throw new JsonParseException("expected ':'", position);

            position++;
            members[name] = ParseValue(text, ref position);

            SkipWhitespace(text, ref position);

            var next = Peek(text, position);

            if (next == ',') { position++; continue; }
            if (next == '}') { position++; break; }

            throw new JsonParseException("expected ',' or '}'", position);
        }

        return JsonValue.FromObject(members);
    }

    static JsonValue ParseArray(string text, ref int position) {
        position++; // '['
        var items = new List<JsonValue>();

        SkipWhitespace(text, ref position);

        if (Peek(text, position) == ']') {
            position++;
            return JsonValue.FromArray(items);
        }

        while (true) {
            items.Add(ParseValue(text, ref position));

            SkipWhitespace(text, ref position);

            var next = Peek(text, position);

            if (next == ',') { position++; continue; }
            if (next == ']') { position++; break; }

            throw new JsonParseException("expected ',' or ']'", position);
        }

        return JsonValue.FromArray(items);
    }

    static string ParseString(string text, ref int position) {
        position++; // opening quote
        var builder = new StringBuilder();

        while (true) {
            if (position >= text.Length) throw new JsonParseException("unterminated string", position);

            var current = text[position];

            if (current == '"') { position++; break; }

            if (current != '\\') {
                builder.Append(current);
                position++;
                continue;
            }

            position++;

            if (position >= text.Length) throw new JsonParseException("unterminated escape sequence", position);

            var escape = text[position++];

            switch (escape) {
                case '"':  builder.Append('"');  break;
                case '\\': builder.Append('\\'); break;
                case '/':  builder.Append('/');  break;
                case 'b':  builder.Append('\b'); break;
                case 'f':  builder.Append('\f'); break;
                case 'n':  builder.Append('\n'); break;
                case 'r':  builder.Append('\r'); break;
                case 't':  builder.Append('\t'); break;
                case 'u':
                    if (position + 4 > text.Length) throw new JsonParseException("truncated \\u escape", position);

                    builder.Append((char)int.Parse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    position += 4;
                    break;

                default: throw new JsonParseException($"invalid escape '\\{escape}'", position - 1);
            }
        }

        return builder.ToString();
    }

    static double ParseNumber(string text, ref int position) {
        var start = position;

        if (Peek(text, position) is '-' or '+') position++;

        while (position < text.Length && (char.IsDigit(text[position]) || text[position] is '.' or 'e' or 'E' or '-' or '+'))
            position++;

        var slice = text.Substring(start, position - start);

        if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new JsonParseException($"invalid number '{slice}'", start);

        return value;
    }

    static JsonValue ParseLiteral(string text, ref int position, string literal, JsonValue value) {
        if (position + literal.Length > text.Length || text.Substring(position, literal.Length) != literal)
            throw new JsonParseException("invalid literal", position);

        position += literal.Length;
        return value;
    }

    static void SkipWhitespace(string text, ref int position) {
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }

    static char Peek(string text, int position) => position < text.Length ? text[position] : '\0';
}

sealed class JsonParseException(string message, int offset) : System.Exception(message) {
    /// <summary>Character offset into the schema text, used to place the diagnostic.</summary>
    public int Offset { get; } = offset;
}
