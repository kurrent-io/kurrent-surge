namespace Kurrent.Client;

/// <summary>
/// A parsed property path expression — a column name with an optional key
/// for map-type columns (e.g., <c>properties['region']</c>).
/// </summary>
/// <param name="PropertyName">
/// The column name. May be a canonical name, an alias, or a dot-path
/// (e.g., <c>rec.position.stream</c>). Resolution is case-insensitive.
/// </param>
/// <param name="Key">
/// Optional map key for keyed columns like <c>properties</c>/<c>metadata</c>.
/// Populated from bracket notation (<c>props['key']</c>) or dot notation
/// (<c>props.key</c>).
/// </param>
[PublicAPI]
public sealed record PropertyAccess(string PropertyName, string? Key = null) {
    /// <summary>
    /// Parses a property path expression into a <see cref="PropertyAccess"/>.
    /// </summary>
    /// <param name="expression">
    /// A property path such as <c>"stream"</c>, <c>"properties['tenantId']"</c>,
    /// or <c>"props.region"</c>.
    /// </param>
    /// <returns>The parsed property access.</returns>
    /// <exception cref="ArgumentException">Thrown when the expression is null, empty, or invalid.</exception>
    public static PropertyAccess Parse(string expression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var parser = new Parser(expression);
        return parser.Parse();
    }

    /// <summary>
    /// Tries to parse a property path expression into a <see cref="PropertyAccess"/>.
    /// </summary>
    /// <param name="expression">The property path expression.</param>
    /// <param name="result">The parsed property access, or <c>null</c> if parsing failed.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    public static bool TryParse(string expression, out PropertyAccess? result) {
        result = null;

        if (string.IsNullOrWhiteSpace(expression))
            return false;

        try {
            result = Parse(expression);
            return true;
        }
        catch (ArgumentException) {
            return false;
        }
    }

    #region ->> Parser <<-

    ref struct Parser(string input) {
        int _position;

        char Current => _position < input.Length ? input[_position] : '\0';
        bool IsAtEnd => _position >= input.Length;

        public PropertyAccess Parse() {
            SkipWhitespace();

            var identifier = ReadIdentifier();
            if (string.IsNullOrEmpty(identifier))
                throw new ArgumentException($"Expected identifier at position {_position}", nameof(input));

            SkipWhitespace();
            string? key = null;

            if (!IsAtEnd) {
                if (Current == '[')
                    key = ReadBracketKey();
                else if (Current == '.')
                    key = ReadDotKey();
            }

            SkipWhitespace();
            if (!IsAtEnd)
                throw new ArgumentException(
                    $"Unexpected token '{Current}' at position {_position}", nameof(input));

            return new(identifier, key);
        }

        string ReadIdentifier() {
            var start = _position;

            if (!char.IsLetter(Current) && Current != '_')
                return "";

            while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
                _position++;

            return input[start.._position];
        }

        string ReadBracketKey() {
            _position++; // Skip '['
            SkipWhitespace();

            if (Current != '\'')
                throw new ArgumentException($"Expected quote at position {_position}", nameof(input));

            _position++; // Skip opening quote

            var start      = _position;
            var hasEscapes = false;

            while (!IsAtEnd && Current != '\'') {
                if (Current == '\\' && _position + 1 < input.Length) {
                    hasEscapes = true;
                    _position++;
                }

                _position++;
            }

            if (Current != '\'')
                throw new ArgumentException($"Unterminated string at position {_position}", nameof(input));

            var result = hasEscapes
                ? Unescape(input.AsSpan()[start.._position])
                : input[start.._position];

            _position++; // Skip closing quote
            SkipWhitespace();

            if (Current != ']')
                throw new ArgumentException($"Expected ']' at position {_position}", nameof(input));

            _position++; // Skip ']'

            return result;
        }

        static string Unescape(ReadOnlySpan<char> span) {
            Span<char> buffer = stackalloc char[span.Length];
            var len = 0;

            for (var i = 0; i < span.Length; i++) {
                if (span[i] == '\\' && i + 1 < span.Length)
                    i++;

                buffer[len++] = span[i];
            }

            return new(buffer[..len]);
        }

        string ReadDotKey() {
            _position++; // Skip '.'
            SkipWhitespace();

            var key = ReadIdentifier();
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException($"Expected identifier at position {_position}", nameof(input));

            return key;
        }

        void SkipWhitespace() {
            while (!IsAtEnd && char.IsWhiteSpace(Current))
                _position++;
        }
    }

    #endregion
}
