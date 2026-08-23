// ReSharper disable InconsistentNaming

using System.Buffers;
using System.Collections.Frozen;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Token types produced by <see cref="FilterTokenizer"/>.
/// Covers literals, comparison/logical/set operators, pattern matching, delimiters, and control tokens.
/// </summary>
internal enum TokenType {
    // Literals
    String,
    Number,
    True,
    False,
    Null,
    Identifier,

    // Comparison operators
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,

    // Logical operators
    And,
    Or,
    Not,

    // Pattern matching
    Like,
    ILike,

    // Null checking
    Is,

    // Set and range operators
    In,
    Between,

    // JSON access operator
    ArrowText, // ->> (extract as text, DuckDB compatible)

    // Delimiters
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Dot,

    // Control
    EndOfInput,
    Error
}

/// <summary>
/// A single lexical token produced by the tokenizer, carrying its type, raw text value,
/// and character position in the original input for error reporting.
/// </summary>
internal readonly record struct Token(TokenType Type, string Value, int Position) {
    public static Token EndOfInput(int position)            => new(TokenType.EndOfInput, "", position);
    public static Token Error(string message, int position) => new(TokenType.Error, message, position);
}

/// <summary>
/// Lexer that converts a DuckDB-compatible filter expression into a flat token list.
/// Handles single-character operators, multi-character operators (<c>&lt;&gt;</c>, <c>&lt;=</c>, <c>>=</c>, <c>!=</c>),
/// single-quoted string literals with backslash escaping, numeric literals (including negative and decimal),
/// and SQL-style keywords (<c>AND</c>, <c>OR</c>, <c>NOT</c>, <c>LIKE</c>, <c>ILIKE</c>, <c>IN</c>,
/// <c>BETWEEN</c>, <c>IS</c>, <c>NULL</c>, <c>TRUE</c>, <c>FALSE</c>).
/// </summary>
internal sealed class FilterTokenizer(ReadOnlySpan<char> input) {
    readonly string _input = input.ToString();

    int _position;

    static readonly FrozenDictionary<string, TokenType> Keywords =
        new Dictionary<string, TokenType>(StringComparer.OrdinalIgnoreCase) {
            ["AND"]     = TokenType.And,
            ["OR"]      = TokenType.Or,
            ["NOT"]     = TokenType.Not,
            ["LIKE"]    = TokenType.Like,
            ["ILIKE"]   = TokenType.ILike,
            ["IN"]      = TokenType.In,
            ["BETWEEN"] = TokenType.Between,
            ["IS"]      = TokenType.Is,
            ["NULL"]    = TokenType.Null,
            ["TRUE"]    = TokenType.True,
            ["FALSE"]   = TokenType.False,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    static readonly FrozenDictionary<string, TokenType>.AlternateLookup<ReadOnlySpan<char>> KeywordLookup =
        Keywords.GetAlternateLookup<ReadOnlySpan<char>>();

    static readonly SearchValues<char> WhitespaceChars =
        SearchValues.Create(" \t\n\r\f\v");

    static readonly SearchValues<char> IdentifierChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    static readonly SearchValues<char> DigitChars =
        SearchValues.Create("0123456789");

    char Current => _position < _input.Length ? _input[_position] : '\0';
    bool IsAtEnd => _position >= _input.Length;

    /// <summary>
    /// Scans the entire input and returns all tokens, terminated by an <see cref="TokenType.EndOfInput"/> token.
    /// Stops early on <see cref="TokenType.Error"/> tokens.
    /// </summary>
    public List<Token> Tokenize() {
        var tokens = new List<Token>();

        while (!IsAtEnd) {
            SkipWhitespace();
            if (IsAtEnd) break;

            var token = NextToken();
            tokens.Add(token);
            if (token.Type == TokenType.Error) break;
        }

        tokens.Add(Token.EndOfInput(_position));
        return tokens;
    }

    Token NextToken() {
        var start = _position;
        var ch    = Current;

        switch (ch) {
            case '(':
                Advance();
                return new(TokenType.LeftParen, "(", start);

            case ')':
                Advance();
                return new(TokenType.RightParen, ")", start);

            case '[':
                Advance();
                return new(TokenType.LeftBracket, "[", start);

            case ']':
                Advance();
                return new(TokenType.RightBracket, "]", start);

            case ',':
                Advance();
                return new(TokenType.Comma, ",", start);

            case '.':
                Advance();
                return new(TokenType.Dot, ".", start);

            case '=':
                Advance();
                return new(TokenType.Equal, "=", start);

            case '!':
                Advance();
                if (Current == '=') {
                    Advance();
                    return new(TokenType.NotEqual, "!=", start);
                }

                return Token.Error("Expected '=' after '!'", start);

            case '<':
                Advance();
                if (Current == '=') {
                    Advance();
                    return new(TokenType.LessOrEqual, "<=", start);
                }

                if (Current == '>') {
                    Advance();
                    return new(TokenType.NotEqual, "<>", start);
                }

                return new(TokenType.LessThan, "<", start);

            case '>':
                Advance();
                if (Current == '=') {
                    Advance();
                    return new(TokenType.GreaterOrEqual, ">=", start);
                }

                return new(TokenType.GreaterThan, ">", start);

            case '-':
                if (_position + 1 < _input.Length && _input[_position + 1] == '>') {
                    Advance(); Advance();
                    if (!IsAtEnd && Current == '>') {
                        Advance();
                        return new(TokenType.ArrowText, "->>", start);
                    }

                    // -> is not supported; ->> and dot notation cover all filter use cases
                    return Token.Error("Use '->>' instead of '->'", start);
                }

                if (_position + 1 < _input.Length && char.IsDigit(_input[_position + 1]))
                    return ReadNumber();

                return Token.Error("Unexpected character '-'", start);

            case '\'': return ReadString();
        }

        if (char.IsDigit(ch))
            return ReadNumber();

        if (char.IsLetter(ch) || ch == '_')
            return ReadIdentifierOrKeyword();

        return Token.Error($"Unexpected character '{ch}'", start);
    }

    Token ReadString() {
        var start = _position;
        Advance(); // skip opening quote
        var valueStart = _position;

        // fast scan: look for closing quote, bail on escape char
        while (!IsAtEnd && Current != '\'' && Current != '\\') Advance();

        if (!IsAtEnd && Current == '\'') {
            // no escapes — direct substring
            var value = _input[valueStart.._position];
            Advance();
            return new(TokenType.String, value, start);
        }

        if (IsAtEnd) return Token.Error("Unterminated string literal", start);

        // slow path: has escape sequences
        var sb = new System.Text.StringBuilder();
        sb.Append(_input.AsSpan(valueStart, _position - valueStart));

        while (!IsAtEnd && Current != '\'') {
            if (Current == '\\' && _position + 1 < _input.Length)
                Advance();

            sb.Append(Current);

            Advance();
        }

        if (IsAtEnd) return Token.Error("Unterminated string literal", start);

        Advance();
        return new(TokenType.String, sb.ToString(), start);
    }

    Token ReadNumber() {
        var start = _position;
        if (Current == '-') Advance();

        var remaining = _input.AsSpan(_position);
        var idx = remaining.IndexOfAnyExcept(DigitChars);
        _position += idx < 0 ? remaining.Length : idx;

        if (!IsAtEnd && Current == '.' && _position + 1 < _input.Length && char.IsDigit(_input[_position + 1])) {
            Advance();
            remaining = _input.AsSpan(_position);
            idx = remaining.IndexOfAnyExcept(DigitChars);
            _position += idx < 0 ? remaining.Length : idx;
        }

        return new(TokenType.Number, _input[start.._position], start);
    }

    Token ReadIdentifierOrKeyword() {
        var start = _position;

        var remaining = _input.AsSpan(_position);
        var idx = remaining.IndexOfAnyExcept(IdentifierChars);
        _position += idx < 0 ? remaining.Length : idx;

        var span  = _input.AsSpan(start, _position - start);
        var value = new string(span);

        return KeywordLookup.TryGetValue(span, out var keywordType)
            ? new(keywordType, value, start)
            : new Token(TokenType.Identifier, value, start);
    }

    void SkipWhitespace() {
        var remaining = _input.AsSpan(_position);
        var idx = remaining.IndexOfAnyExcept(WhitespaceChars);
        _position += idx < 0 ? remaining.Length : idx;
    }

    void Advance() => _position++;
}
