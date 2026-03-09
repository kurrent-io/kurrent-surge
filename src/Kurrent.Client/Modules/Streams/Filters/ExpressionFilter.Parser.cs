// ReSharper disable InconsistentNaming

using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Parser for DuckDB-compatible filter expressions using a Pratt parser.
/// </summary>
[PublicAPI]
public sealed class ExpressionFilterParser {
    /// <summary>
    /// Parses a filter expression from a character span into an executable filter.
    /// </summary>
    public Result<IFilterExpression, ExpressionFilterParseError> Parse(ReadOnlySpan<char> input) {
        if (input.IsEmpty || input.IsWhiteSpace())
            return ExpressionFilterParseError.EmptyInput();

        try {
            var tokenizer = new FilterTokenizer(input);
            var tokens    = tokenizer.Tokenize();

            var errorToken = tokens.FirstOrDefault(t => t.Type == TokenType.Error);
            if (errorToken.Type == TokenType.Error)
                return ExpressionFilterParseError.SyntaxError(errorToken.Value, errorToken.Position);

            var parser = new PrattParser(tokens);
            return Result.Success<IFilterExpression, ExpressionFilterParseError>(parser.Parse());
        }
        catch (ParseException ex) {
            return ExpressionFilterParseError.SyntaxError(ex.NearToken, ex.Position);
        }
    }

}

/// <summary>
/// Represents an error that occurred while parsing a filter expression.
/// Error messages follow DuckDB format: "Error parsing filter: syntax error at or near 'TOKEN'"
/// </summary>
[PublicAPI]
public sealed record ExpressionFilterParseError : IResultError {
    /// <summary>
    /// Gets the error code for filter parse errors.
    /// </summary>
    public ErrorCode ErrorCode { get; } = ErrorCode.FromType<ExpressionFilterParseError>();

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Gets the position in the input where the error occurred.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Gets the token near which the error occurred.
    /// </summary>
    public string? NearToken { get; }

    /// <summary>
    /// Gets metadata associated with this error.
    /// </summary>
    public ImmutableDictionary<string, object?> Metadata { get; }

    ExpressionFilterParseError(string errorMessage, int position, string? nearToken) {
        ErrorMessage = errorMessage;
        Position     = position;
        NearToken    = nearToken;

        var builder = ImmutableDictionary.CreateBuilder<string, object?>();
        builder["Position"] = position;

        if (nearToken is not null)
            builder["NearToken"] = nearToken;

        Metadata = builder.ToImmutable();
    }

    /// <summary>
    /// Creates a syntax error at or near the specified token.
    /// </summary>
    /// <param name="nearToken">The token near which the error occurred.</param>
    /// <param name="position">The position in the input where the error occurred.</param>
    /// <returns>A new <see cref="ExpressionFilterParseError"/> instance.</returns>
    public static ExpressionFilterParseError SyntaxError(string nearToken, int position = 0) =>
        new($"Error parsing filter: syntax error at or near \"{nearToken}\"", position, nearToken);

    /// <summary>
    /// Creates a syntax error for unexpected end of input.
    /// </summary>
    /// <param name="position">The position in the input where the error occurred.</param>
    /// <returns>A new <see cref="ExpressionFilterParseError"/> instance.</returns>
    public static ExpressionFilterParseError UnexpectedEndOfInput(int position = 0) =>
        new("Error parsing filter: syntax error at or near \"end of input\"", position, "end of input");

    /// <summary>
    /// Creates an error for empty input.
    /// </summary>
    /// <returns>A new <see cref="ExpressionFilterParseError"/> instance.</returns>
    public static ExpressionFilterParseError EmptyInput() =>
        new("Error parsing filter: empty expression", 0, null);

    /// <summary>
    /// Creates a custom error with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The position in the input where the error occurred.</param>
    /// <returns>A new <see cref="ExpressionFilterParseError"/> instance.</returns>
    public static ExpressionFilterParseError Custom(string message, int position = 0) =>
        new($"Error parsing filter: {message}", position, null);

    /// <inheritdoc />
    public Exception GetOrCreateException() =>
        new FormatException(ErrorMessage);
}

/// <summary>
/// Represents a filter expression that can be evaluated against a record.
/// </summary>
[PublicAPI]
public interface IFilterExpression {
    /// <summary>
    /// Evaluates whether the specified record matches this filter expression.
    /// </summary>
    /// <param name="record">The record to evaluate.</param>
    /// <returns><c>true</c> if the record matches; otherwise, <c>false</c>.</returns>
    bool IsMatch(Record record);
}

#region . Tokenizer .

/// <summary>
/// Token types produced by <see cref="FilterTokenizer"/>.
/// Covers literals, comparison/logical/set operators, pattern matching, delimiters, and control tokens.
/// </summary>
file enum TokenType {
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
file readonly record struct Token(TokenType Type, string Value, int Position) {
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
file sealed class FilterTokenizer(ReadOnlySpan<char> input) {
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

#if NET9_0_OR_GREATER
    static readonly FrozenDictionary<string, TokenType>.AlternateLookup<ReadOnlySpan<char>> KeywordLookup =
        Keywords.GetAlternateLookup<ReadOnlySpan<char>>();

    static readonly SearchValues<char> WhitespaceChars =
        SearchValues.Create(" \t\n\r\f\v");

    static readonly SearchValues<char> IdentifierChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    static readonly SearchValues<char> DigitChars =
        SearchValues.Create("0123456789");
#endif

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
                return new Token(TokenType.LeftParen, "(", start);

            case ')':
                Advance();
                return new Token(TokenType.RightParen, ")", start);

            case '[':
                Advance();
                return new Token(TokenType.LeftBracket, "[", start);

            case ']':
                Advance();
                return new Token(TokenType.RightBracket, "]", start);

            case ',':
                Advance();
                return new Token(TokenType.Comma, ",", start);

            case '.':
                Advance();
                return new Token(TokenType.Dot, ".", start);

            case '=':
                Advance();
                return new Token(TokenType.Equal, "=", start);

            case '!':
                Advance();
                if (Current == '=') {
                    Advance();
                    return new Token(TokenType.NotEqual, "!=", start);
                }

                return Token.Error("Expected '=' after '!'", start);

            case '<':
                Advance();
                if (Current == '=') {
                    Advance();
                    return new Token(TokenType.LessOrEqual, "<=", start);
                }

                if (Current == '>') {
                    Advance();
                    return new Token(TokenType.NotEqual, "<>", start);
                }

                return new Token(TokenType.LessThan, "<", start);

            case '>':
                Advance();
                if (Current == '=') {
                    Advance();
                    return new Token(TokenType.GreaterOrEqual, ">=", start);
                }

                return new Token(TokenType.GreaterThan, ">", start);

            case '\'': return ReadString();
        }

        if (char.IsDigit(ch) || (ch == '-' && _position + 1 < _input.Length && char.IsDigit(_input[_position + 1])))
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
            return new Token(TokenType.String, value, start);
        }

        if (IsAtEnd) return Token.Error("Unterminated string literal", start);

        // slow path: has escape sequences
        var sb = new System.Text.StringBuilder();
        sb.Append(_input.AsSpan(valueStart, _position - valueStart));

        while (!IsAtEnd && Current != '\'') {
            if (Current == '\\' && _position + 1 < _input.Length) {
                Advance();
                sb.Append(Current);
            }
            else
                sb.Append(Current);

            Advance();
        }

        if (IsAtEnd) return Token.Error("Unterminated string literal", start);

        Advance();
        return new Token(TokenType.String, sb.ToString(), start);
    }

    Token ReadNumber() {
        var start = _position;
        if (Current == '-') Advance();

#if NET9_0_OR_GREATER
        var remaining = _input.AsSpan(_position);
        var idx = remaining.IndexOfAnyExcept(DigitChars);
        _position += idx < 0 ? remaining.Length : idx;

        if (!IsAtEnd && Current == '.' && _position + 1 < _input.Length && char.IsDigit(_input[_position + 1])) {
            Advance();
            remaining = _input.AsSpan(_position);
            idx = remaining.IndexOfAnyExcept(DigitChars);
            _position += idx < 0 ? remaining.Length : idx;
        }
#else
        while (!IsAtEnd && char.IsDigit(Current)) Advance();

        if (!IsAtEnd && Current == '.' && _position + 1 < _input.Length && char.IsDigit(_input[_position + 1])) {
            Advance();
            while (!IsAtEnd && char.IsDigit(Current)) Advance();
        }
#endif

        return new Token(TokenType.Number, _input[start.._position], start);
    }

    Token ReadIdentifierOrKeyword() {
        var start = _position;

#if NET9_0_OR_GREATER
        var remaining = _input.AsSpan(_position);
        var idx = remaining.IndexOfAnyExcept(IdentifierChars);
        _position += idx < 0 ? remaining.Length : idx;

        var span  = _input.AsSpan(start, _position - start);
        var value = new string(span);

        return KeywordLookup.TryGetValue(span, out var keywordType)
            ? new Token(keywordType, value, start)
            : new Token(TokenType.Identifier, value, start);
#else
        while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_')) Advance();
        var value = _input[start.._position];
        return new Token(GetKeywordType(value), value, start);
#endif
    }

    static TokenType GetKeywordType(string value) =>
        Keywords.TryGetValue(value, out var type) ? type : TokenType.Identifier;

    void SkipWhitespace() {
#if NET9_0_OR_GREATER
        var remaining = _input.AsSpan(_position);
        var idx = remaining.IndexOfAnyExcept(WhitespaceChars);
        _position += idx < 0 ? remaining.Length : idx;
#else
        while (!IsAtEnd && char.IsWhiteSpace(Current)) Advance();
#endif
    }

    void Advance() => _position++;
}

#endregion

#region . Pratt Parser .

/// <summary>
/// Internal exception thrown by the parser when it encounters an unexpected token.
/// Caught by <see cref="ExpressionFilterParser.Parse"/> and converted to
/// <see cref="ExpressionFilterParseError"/>.
/// </summary>
file sealed class ParseException(string nearToken, int position) : Exception {
    public string NearToken { get; } = nearToken;
    public int    Position  { get; } = position;
}

/// <summary>
/// Pratt (top-down operator precedence) parser that transforms a token list into an AST
/// of <see cref="IFilterExpression"/> nodes. Binding powers: OR (10), AND (20), NOT (60).
/// Comparison, LIKE, IN, BETWEEN, and IS NULL are parsed as prefix property expressions.
/// </summary>
file sealed class PrattParser(List<Token> tokens) {
    int _current;

    /// <summary>
    /// Parses the full token stream into a single filter expression AST.
    /// Throws <see cref="ParseException"/> if tokens remain after the expression is fully parsed.
    /// </summary>
    public IFilterExpression Parse() {
        var result = ParseExpression(0);

        if (Peek().Type == TokenType.EndOfInput) return result;

        var token = Peek();

        throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
    }

    /// <summary>
    /// Core Pratt loop. Parses prefix then consumes infix operators while their binding power
    /// exceeds <paramref name="minBindingPower"/>.
    /// </summary>
    IFilterExpression ParseExpression(int minBindingPower) {
        var left = ParsePrefix();

        while (true) {
            var token = Peek();
            var bp    = GetInfixBindingPower(token.Type);

            if (bp <= minBindingPower)
                break;

            left = ParseInfix(left, token);
        }

        return left;
    }

    /// <summary>
    /// Parses a prefix expression: parenthesized group, NOT, or a property-based predicate.
    /// </summary>
    IFilterExpression ParsePrefix() {
        var token = Peek();

        switch (token.Type) {
            case TokenType.LeftParen:
                Advance();
                var expr = ParseExpression(0);
                Expect(TokenType.RightParen, ")");
                return expr;

            case TokenType.Not:
                Advance();
                if (Peek().Type == TokenType.LeftParen) {
                    Advance();
                    var inner = ParseExpression(0);
                    Expect(TokenType.RightParen, ")");
                    return new NotExpression(inner);
                }

                return new NotExpression(ParseExpression(60));

            case TokenType.Identifier:
                return ParsePropertyExpression();

            default:
                throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
        }
    }

    /// <summary>
    /// Parses an infix operator (AND, OR) combining <paramref name="left"/> with the right operand.
    /// </summary>
    IFilterExpression ParseInfix(IFilterExpression left, Token token) {
        switch (token.Type) {
            case TokenType.And:
                Advance();
                return new AndExpression(left, ParseExpression(20));

            case TokenType.Or:
                Advance();
                return new OrExpression(left, ParseExpression(10));

            default:
                throw new ParseException(token.Value, token.Position);
        }
    }

    /// <summary>
    /// Parses a property reference followed by its operator and operand(s).
    /// Dispatches to specialized parsers based on the operator token:
    /// comparison, IS NULL, IN, NOT IN/LIKE/BETWEEN, LIKE, ILIKE, BETWEEN.
    /// Intercepts <c>index</c> as a special routing column before normal dispatch.
    /// </summary>
    IFilterExpression ParsePropertyExpression() {
        var property = ParsePropertyAccess();

        if (property.PropertyName.Equals("index", StringComparison.OrdinalIgnoreCase))
            return ParseIndexExpression();

        var token = Peek();

        switch (token.Type) {
            case TokenType.Is:
                return ParseIsExpression(property);

            case TokenType.In:
                Advance();
                return ParseInExpression(property, false);

            case TokenType.Not:
                return ParseNotPrefixedOperator(property);

            case TokenType.Like:
                Advance();
                return ParseLikeExpression(property, false, false);

            case TokenType.ILike:
                Advance();
                return ParseLikeExpression(property, true, false);

            case TokenType.Between:
                Advance();
                return ParseBetweenExpression(property, false);

            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.GreaterThan:
            case TokenType.LessThan:
            case TokenType.GreaterOrEqual:
            case TokenType.LessOrEqual:
                return ParseComparisonExpression(property);

            default:
                throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
        }
    }

    /// <summary>
    /// Parses <c>index = 'name'</c> into an <see cref="IndexExpression"/>.
    /// The index column is a routing directive — it always matches client-side and is
    /// extracted server-side to target <c>$idx-user-*</c> streams.
    /// </summary>
    IndexExpression ParseIndexExpression() {
        Expect(TokenType.Equal, "=");
        var name = Expect(TokenType.String, "index name");
        return new IndexExpression(name.Value);
    }

    /// <summary>
    /// Parses a column reference with optional key access. Supports three forms:
    /// <list type="bullet">
    ///   <item>Simple identifier: <c>stream</c></item>
    ///   <item>Bracket key access: <c>properties['region']</c></item>
    ///   <item>Dot-path: <c>rec.properties.region</c> — all segments are joined into the name.
    ///     Resolution splits name from key at resolve time using the column registry.</item>
    /// </list>
    /// </summary>
    PropertyAccess ParsePropertyAccess() {
        var     identifier = Expect(TokenType.Identifier, "identifier");
        var     name       = identifier.Value;
        string? key        = null;

        if (Peek().Type == TokenType.LeftBracket) {
            Advance();
            var keyToken = Expect(TokenType.String, "string key");
            key = keyToken.Value;
            Expect(TokenType.RightBracket, "]");
        }
        else if (Peek().Type == TokenType.Dot) {
            var segments = new List<string> { name };
            while (Peek().Type == TokenType.Dot) {
                Advance();
                var segment = Expect(TokenType.Identifier, "property segment");
                segments.Add(segment.Value);
            }

            name = string.Join(".", segments);
        }

        return new PropertyAccess(name, key);
    }

    /// <summary>
    /// Parses <c>property IS [NOT] NULL</c>.
    /// </summary>
    IFilterExpression ParseIsExpression(PropertyAccess property) {
        Advance();

        var negated = false;
        if (Peek().Type == TokenType.Not) {
            Advance();
            negated = true;
        }

        Expect(TokenType.Null, "NULL");
        return new IsNullExpression(property, negated);
    }

    /// <summary>
    /// Parses negated operators: <c>NOT IN</c>, <c>NOT LIKE</c>, <c>NOT ILIKE</c>, <c>NOT BETWEEN</c>.
    /// </summary>
    IFilterExpression ParseNotPrefixedOperator(PropertyAccess property) {
        Advance();

        var token = Peek();
        switch (token.Type) {
            case TokenType.In:
                Advance();
                return ParseInExpression(property, true);

            case TokenType.Like:
                Advance();
                return ParseLikeExpression(property, false, true);

            case TokenType.ILike:
                Advance();
                return ParseLikeExpression(property, true, true);

            case TokenType.Between:
                Advance();
                return ParseBetweenExpression(property, true);

            default:
                throw new ParseException(token.Value, token.Position);
        }
    }

    /// <summary>
    /// Parses <c>property op value</c> where op is <c>=</c>, <c>!=</c>/<c>&lt;&gt;</c>,
    /// <c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, or <c>&lt;=</c>.
    /// </summary>
    IFilterExpression ParseComparisonExpression(PropertyAccess property) {
        var opToken = Advance();
        var op = opToken.Type switch {
            TokenType.Equal          => ComparisonOperator.Equal,
            TokenType.NotEqual       => ComparisonOperator.NotEqual,
            TokenType.GreaterThan    => ComparisonOperator.GreaterThan,
            TokenType.LessThan       => ComparisonOperator.LessThan,
            TokenType.GreaterOrEqual => ComparisonOperator.GreaterThanOrEqual,
            TokenType.LessOrEqual    => ComparisonOperator.LessThanOrEqual,
            _                        => throw new ParseException(opToken.Value, opToken.Position)
        };

        var value = ParseValue();
        return new ComparisonExpression(property, op, value);
    }

    /// <summary>
    /// Parses <c>property [NOT] LIKE|ILIKE 'pattern'</c>. Patterns use SQL wildcards:
    /// <c>%</c> (any sequence) and <c>_</c> (single character).
    /// </summary>
    IFilterExpression ParseLikeExpression(PropertyAccess property, bool caseInsensitive, bool negated) {
        var pattern = Expect(TokenType.String, "pattern string");
        return new LikeExpression(
            property, pattern.Value, caseInsensitive,
            negated
        );
    }

    /// <summary>
    /// Parses <c>property [NOT] IN (value, value, ...)</c>.
    /// At least one value is required; empty lists produce a parse error.
    /// </summary>
    IFilterExpression ParseInExpression(PropertyAccess property, bool negated) {
        Expect(TokenType.LeftParen, "(");

        var values = new List<FilterValue>();

        if (Peek().Type == TokenType.RightParen)
            throw new ParseException(")", Peek().Position);

        values.Add(ParseValue());

        while (Peek().Type == TokenType.Comma) {
            Advance();
            values.Add(ParseValue());
        }

        Expect(TokenType.RightParen, ")");
        return new InExpression(property, values, negated);
    }

    /// <summary>
    /// Parses <c>property [NOT] BETWEEN lower AND upper</c> (inclusive range check).
    /// </summary>
    IFilterExpression ParseBetweenExpression(PropertyAccess property, bool negated) {
        var lower = ParseValue();
        Expect(TokenType.And, "AND");
        var upper = ParseValue();
        return new BetweenExpression(
            property, lower, upper,
            negated
        );
    }

    /// <summary>
    /// Parses a literal value: single-quoted string, number, <c>TRUE</c>, <c>FALSE</c>, or <c>NULL</c>.
    /// </summary>
    FilterValue ParseValue() {
        var token = Peek();

        switch (token.Type) {
            case TokenType.String:
                Advance();
                return new StringFilterValue(token.Value);

            case TokenType.Number:
                Advance();
                return new NumberFilterValue(decimal.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture));

            case TokenType.True:
                Advance();
                return new BooleanFilterValue(true);

            case TokenType.False:
                Advance();
                return new BooleanFilterValue(false);

            case TokenType.Null:
                Advance();
                return NullFilterValue.Instance;

            default:
                throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
        }
    }

    /// <summary>
    /// Returns the binding power for infix operators. OR binds at 10, AND at 20.
    /// All other tokens return 0, terminating the Pratt loop.
    /// </summary>
    static int GetInfixBindingPower(TokenType type) =>
        type switch {
            TokenType.Or  => 10,
            TokenType.And => 20,
            _             => 0
        };

    Token Peek()    => _current < tokens.Count ? tokens[_current] : Token.EndOfInput(0);
    Token Advance() => tokens[_current++];

    Token Expect(TokenType type, string expected) {
        var token = Peek();
        return token.Type != type
            ? throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position)
            : Advance();
    }
}

#endregion

#region . AST Nodes .

/// <summary>
/// Base class for typed literal values in filter expressions.
/// </summary>
public abstract record FilterValue {
    /// <summary>
    /// Returns the boxed CLR value of this literal.
    /// </summary>
    public abstract object? GetValue();
}

/// <summary>
/// A single-quoted string literal value.
/// </summary>
public sealed record StringFilterValue(string Value) : FilterValue {
    /// <inheritdoc/>
    public override object GetValue() => Value;
}

/// <summary>
/// A numeric literal value, stored as <see cref="decimal"/> for lossless comparison.
/// </summary>
sealed record NumberFilterValue(decimal Value) : FilterValue {
    /// <inheritdoc/>
    public override object GetValue() => Value;
}

/// <summary>
/// A boolean literal value (<c>TRUE</c> or <c>FALSE</c>).
/// </summary>
sealed record BooleanFilterValue(bool Value) : FilterValue {
    /// <inheritdoc/>
    public override object GetValue() => Value;
}

/// <summary>
/// The <c>NULL</c> literal value. Singleton via <see cref="Instance"/>.
/// </summary>
sealed record NullFilterValue : FilterValue {
    public static readonly NullFilterValue Instance = new();

    /// <inheritdoc/>
    public override object? GetValue() => null;
}

/// <summary>
/// A parsed column reference in a filter expression, consisting of a property name
/// and an optional key for map-type columns (e.g., <c>properties['region']</c>).
/// </summary>
/// <param name="PropertyName">
/// The column name as written in the expression. May be a canonical name, an alias,
/// or a dot-path (e.g., <c>rec.position.stream</c>). Resolution is case-insensitive.
/// </param>
/// <param name="Key">
/// Optional map key for keyed columns like <c>properties</c>/<c>metadata</c>.
/// Populated from bracket notation (<c>props['key']</c>) or trailing dot segments
/// (<c>rec.properties.key</c>).
/// </param>
public sealed record PropertyAccess(string PropertyName, string? Key = null) {
    static readonly PropertyMap<Record> Columns = PropertyMap<Record>.Build(map =>
        map.Register(r => r.Stream.Value,
                "stream", "stream_name", "rec.stream", "rec.position.stream")
           .Register(r => r.Schema.SchemaName.Value,
                "schema", "schema_name", "event_type", "rec.schema", "rec.schema.name")
           .Register(r => r.Stream.StreamCategory.Value,
                "category", "rec.category")
           .Register(r => r.Timestamp,
                "timestamp", "ts", "created_at", "created_ts", "rec.timestamp", "rec.ts", "rec.created_at")
           .Register(r => r.Position.LogPosition.Value,
                "position", "pos", "log_position", "rec.position", "rec.pos", "rec.log_position")
           .Register(r => r.Revision.Value,
                "revision", "stream_revision", "event_number", "rec.revision", "rec.position.streamrevision")
           .Register(r => r.Id,
                "id", "record_id", "rec.id")
           .RegisterKeyed(ResolveProperty,
                "properties", "props", "metadata", "rec.properties", "rec.props", "rec.metadata")
    );

    /// <summary>
    /// Extracts the value this property references from a record.
    /// </summary>
    public object? Resolve(Record record) => Resolve(PropertyName, Key, record);

    /// <summary>
    /// Attempts to resolve this property as a <see cref="decimal"/> without boxing.
    /// Returns <c>false</c> if the property is not registered or is not numeric.
    /// </summary>
    public bool TryResolveDecimal(Record record, out decimal result) =>
        Columns.TryGetDecimalValue(PropertyName, record, out result);

    static object? Resolve(string name, string? key, Record record) {
        var value = Columns.GetValue(name, key, record);
        if (value is not null) return value;

        // dot-path without explicit key: split right-to-left to find a registered prefix
        if (key is null) {
            for (var i = name.Length - 1; i >= 0; i--) {
                if (name[i] != '.') continue;

                value = Columns.GetValue(name[..i], name[(i + 1)..], record);
                if (value is not null) return value;
            }
        }

        // strip field_ prefix (WebUI compat) and retry
        if (name.StartsWith("field_", StringComparison.OrdinalIgnoreCase))
            return Resolve(name[6..], key, record);

        return null;
    }

    static object? ResolveProperty(Record record, string? key) =>
        key is not null && record.Metadata.TryGetValue<object>(key, out var value)
            ? value
            : null;
}

/// <summary>
/// Comparison operators supported in filter expressions.
/// </summary>
public enum ComparisonOperator {
    /// <summary><c>=</c></summary>
    Equal,
    /// <summary><c>!=</c> or <c>&lt;&gt;</c></summary>
    NotEqual,
    /// <summary><c>&gt;</c></summary>
    GreaterThan,
    /// <summary><c>&lt;</c></summary>
    LessThan,
    /// <summary><c>&gt;=</c></summary>
    GreaterThanOrEqual,
    /// <summary><c>&lt;=</c></summary>
    LessThanOrEqual
}

/// <summary>
/// AST node for <c>property op value</c> comparisons.
/// Uses <see cref="ValueComparer"/> for cross-type comparison (string, numeric, datetime, boolean).
/// </summary>
public sealed class ComparisonExpression(PropertyAccess property, ComparisonOperator op, FilterValue value) : IFilterExpression {
    public PropertyAccess     Property => property;
    public ComparisonOperator Op       => op;
    public FilterValue        Value    => value;

    /// <inheritdoc/>
    public bool IsMatch(Record record) {
        // fast path: category equality via span comparison — no substring allocation
        if (property.PropertyName is "category" or "rec.category"
            && value is StringFilterValue strVal
            && op is ComparisonOperator.Equal or ComparisonOperator.NotEqual) {
            var stream   = record.Stream.Value;
            var idx      = stream.IndexOf('-');
            var category = idx < 0 ? stream.AsSpan() : stream.AsSpan(0, idx);
            var match    = category.Equals(strVal.Value, StringComparison.OrdinalIgnoreCase);
            return op == ComparisonOperator.Equal ? match : !match;
        }

        // fast path: numeric comparison without boxing
        if (value is NumberFilterValue numValue && property.TryResolveDecimal(record, out var numLeft)) {
            var cmp = numLeft.CompareTo(numValue.Value);
            return op switch {
                ComparisonOperator.Equal              => cmp == 0,
                ComparisonOperator.NotEqual           => cmp != 0,
                ComparisonOperator.GreaterThan        => cmp > 0,
                ComparisonOperator.LessThan           => cmp < 0,
                ComparisonOperator.GreaterThanOrEqual => cmp >= 0,
                ComparisonOperator.LessThanOrEqual    => cmp <= 0,
                _                                     => false
            };
        }

        // general path
        var left  = property.Resolve(record);
        var right = value.GetValue();

        if (left is null || right is null) return false;

        var cmpGeneral = ValueComparer.Compare(left, right);

        return op switch {
            ComparisonOperator.Equal              => cmpGeneral == 0,
            ComparisonOperator.NotEqual           => cmpGeneral != 0,
            ComparisonOperator.GreaterThan        => cmpGeneral > 0,
            ComparisonOperator.LessThan           => cmpGeneral < 0,
            ComparisonOperator.GreaterThanOrEqual => cmpGeneral >= 0,
            ComparisonOperator.LessThanOrEqual    => cmpGeneral <= 0,
            _                                     => false
        };
    }
}

/// <summary>
/// Logical AND conjunction. Short-circuits: if <paramref name="left"/> is false,
/// <paramref name="right"/> is not evaluated.
/// </summary>
public sealed class AndExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression {
    public IFilterExpression Left  => left;
    public IFilterExpression Right => right;

    /// <inheritdoc/>
    public bool IsMatch(Record record) => left.IsMatch(record) && right.IsMatch(record);
}

/// <summary>
/// Logical OR disjunction. Short-circuits: if left is true, right is not evaluated.
/// </summary>
file sealed class OrExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression {
    public bool IsMatch(Record record) => left.IsMatch(record) || right.IsMatch(record);
}

/// <summary>
/// Logical NOT negation.
/// </summary>
file sealed class NotExpression(IFilterExpression inner) : IFilterExpression {
    public bool IsMatch(Record record) => !inner.IsMatch(record);
}

/// <summary>
/// AST node for <c>property [NOT] LIKE|ILIKE 'pattern'</c>.
/// Simple patterns (prefix%, %suffix, %contains%, exact) use string operations.
/// Complex patterns (with <c>_</c> wildcards or multiple <c>%</c> segments) fall back to compiled Regex.
/// </summary>
public sealed class LikeExpression(PropertyAccess property, string pattern, bool caseInsensitive, bool negated)
    : IFilterExpression {
    /// <summary>
    /// Classification of SQL LIKE patterns for fast-path evaluation.
    /// Simple patterns use string operations; complex patterns fall back to Regex.
    /// </summary>
    enum PatternKind { Exact, Prefix, Suffix, Contains, MatchAll, Complex }

    readonly PatternKind      _kind       = ClassifyPattern(pattern);
    readonly string           _fixedPart  = ExtractFixedPart(pattern);
    readonly Regex?           _regex      = ClassifyPattern(pattern) == PatternKind.Complex
        ? ConvertLikeToRegex(pattern, caseInsensitive)
        : null;
    readonly StringComparison _comparison = caseInsensitive
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public PropertyAccess Property        => property;
    public string         Pattern         => pattern;
    public bool           CaseInsensitive => caseInsensitive;
    public bool           Negated         => negated;

    /// <inheritdoc/>
    public bool IsMatch(Record record) {
        var value = property.Resolve(record);
        if (value is null) return false;

        var str     = value as string ?? value.ToString() ?? "";
        var matches = _kind switch {
            PatternKind.MatchAll => true,
            PatternKind.Exact    => str.Equals(_fixedPart, _comparison),
            PatternKind.Prefix   => str.StartsWith(_fixedPart, _comparison),
            PatternKind.Suffix   => str.EndsWith(_fixedPart, _comparison),
            PatternKind.Contains => str.Contains(_fixedPart, _comparison),
            PatternKind.Complex  => _regex!.IsMatch(str),
            _                        => false
        };

        return negated ? !matches : matches;
    }

    static PatternKind ClassifyPattern(string pattern) {
        if (pattern.Length == 0) return PatternKind.Exact;
        if (pattern is "%") return PatternKind.MatchAll;

        // _ wildcard forces complex (Regex) path
        if (pattern.Contains('_')) return PatternKind.Complex;

        var firstPercent = pattern.IndexOf('%');
        if (firstPercent < 0) return PatternKind.Exact;

        var lastPercent = pattern.LastIndexOf('%');

        // Single % at end: prefix match
        if (firstPercent == pattern.Length - 1)
            return PatternKind.Prefix;

        // Single % at start, no other %: suffix match
        if (firstPercent == 0 && lastPercent == 0)
            return PatternKind.Suffix;

        // % at start and end only, no % in between: contains match
        if (firstPercent == 0 && lastPercent == pattern.Length - 1
            && pattern.AsSpan(1, pattern.Length - 2).IndexOf('%') < 0)
            return PatternKind.Contains;

        return PatternKind.Complex;
    }

    static string ExtractFixedPart(string pattern) =>
        ClassifyPattern(pattern) switch {
            PatternKind.Prefix   => pattern[..^1],
            PatternKind.Suffix   => pattern[1..],
            PatternKind.Contains => pattern[1..^1],
            PatternKind.Exact    => pattern,
            _                        => pattern
        };

    public static Regex ConvertLikeToRegex(string pattern, bool caseInsensitive) {
        var result = new System.Text.StringBuilder(pattern.Length + 4);
        result.Append('^');

        foreach (var ch in pattern) {
            if (ch == '%')
                result.Append(".*");
            else if (ch == '_')
                result.Append('.');
            else if (IsRegexMetaChar(ch)) {
                result.Append('\\');
                result.Append(ch);
            }
            else
                result.Append(ch);
        }

        result.Append('$');

        var options = RegexOptions.Compiled | RegexOptions.Singleline;
        if (caseInsensitive)
            options |= RegexOptions.IgnoreCase;

        return new Regex(result.ToString(), options);

        static bool IsRegexMetaChar(char ch) =>
            ch is '\\' or '*' or '+' or '?' or '|' or '{' or '}' or '[' or ']' or '(' or ')' or '^' or '$' or '.';
    }
}

/// <summary>
/// AST node for <c>property IS [NOT] NULL</c>.
/// </summary>
file sealed class IsNullExpression(PropertyAccess property, bool negated) : IFilterExpression {
    public bool IsMatch(Record record) {
        var isNull = property.Resolve(record) is null;
        return negated ? !isNull : isNull;
    }
}

/// <summary>
/// AST node for <c>property [NOT] IN (value, ...)</c>. Matches if the property value
/// equals any value in the set (or none when negated).
/// When all values are string literals, uses <see cref="FrozenSet{T}"/> for O(1) lookup.
/// </summary>
public sealed class InExpression(PropertyAccess property, IReadOnlyList<FilterValue> values, bool negated) : IFilterExpression {
    readonly FrozenSet<string>? _stringSet = BuildStringSet(values);

    public PropertyAccess             Property => property;
    public IReadOnlyList<FilterValue> Values   => values;
    public bool                       Negated  => negated;

    /// <inheritdoc/>
    public bool IsMatch(Record record) {
        var propValue = property.Resolve(record);
        if (propValue is null) return false;

        // fast path: all-string set with O(1) lookup
        if (_stringSet is not null && propValue is string strValue) {
            var found = _stringSet.Contains(strValue);
            return negated ? !found : found;
        }

        // general path: linear scan with cross-type comparison
        var foundGeneral = false;
        for (var i = 0; i < values.Count; i++) {
            if (ValueComparer.Compare(propValue, values[i].GetValue()) == 0) {
                foundGeneral = true;
                break;
            }
        }

        return negated ? !foundGeneral : foundGeneral;
    }

    static FrozenSet<string>? BuildStringSet(IReadOnlyList<FilterValue> values) {
        for (var i = 0; i < values.Count; i++) {
            if (values[i] is not StringFilterValue)
                return null;
        }

        var set = new HashSet<string>(values.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++)
            set.Add(((StringFilterValue)values[i]).Value);

        return set.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// AST node for <c>index = 'name'</c>. This is a routing directive, not a data predicate:
/// it always returns <c>true</c> client-side. The <see cref="ExpressionFilter.ServerExtractor"/>
/// extracts it to target <c>$idx-user-{IndexName}</c> streams server-side.
/// </summary>
public sealed record IndexExpression(string IndexName) : IFilterExpression {
    /// <inheritdoc/>
    public bool IsMatch(Record record) => true;
}

/// <summary>
/// AST node for <c>property [NOT] BETWEEN lower AND upper</c> (inclusive range).
/// </summary>
file sealed class BetweenExpression(PropertyAccess property, FilterValue lower, FilterValue upper, bool negated) : IFilterExpression {
    public bool IsMatch(Record record) {
        // fast path: numeric range without boxing
        if (lower is NumberFilterValue lo && upper is NumberFilterValue hi
            && property.TryResolveDecimal(record, out var numValue)) {
            var inRange = numValue >= lo.Value && numValue <= hi.Value;
            return negated ? !inRange : inRange;
        }

        // general path
        var value = property.Resolve(record);
        if (value is null) return false;

        var inRangeGeneral = ValueComparer.Compare(value, lower.GetValue()) >= 0 &&
                             ValueComparer.Compare(value, upper.GetValue()) <= 0;

        return negated ? !inRangeGeneral : inRangeGeneral;
    }
}

#endregion

#region . Utilities .

/// <summary>
/// Cross-type value comparator for filter evaluation. Comparison priority:
/// <list type="number">
///   <item>Null handling (null &lt; non-null)</item>
///   <item>String comparison (case-insensitive ordinal)</item>
///   <item>Numeric coercion to <see cref="decimal"/> (int, long, double, float, parseable strings)</item>
///   <item>DateTime coercion (<see cref="DateTime"/>, <see cref="DateTimeOffset"/>, parseable strings)</item>
///   <item>Boolean comparison</item>
///   <item>Fallback: ToString comparison (case-insensitive)</item>
/// </list>
/// </summary>
file static class ValueComparer {
    /// <summary>
    /// Compares two boxed values using type-aware coercion. Returns negative, zero, or positive.
    /// </summary>
    public static int Compare(object? left, object? right) {
        if (left is null && right is null) return 0;

        if (left is null) return -1;

        if (right is null) return 1;

        if (left is string ls && right is string rs)
            return string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);

        if (TryGetNumeric(left, out var ln) && TryGetNumeric(right, out var rn))
            return ln.CompareTo(rn);

        if (TryGetDateTime(left, out var ld) && TryGetDateTime(right, out var rd))
            return ld.CompareTo(rd);

        if (left is bool lb && right is bool rb)
            return lb.CompareTo(rb);

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    static bool TryGetNumeric(object value, out decimal result) {
        result = 0;
        switch (value) {
            case decimal d:
                result = d;
                return true;

            case int i:
                result = i;
                return true;

            case long l:
                result = l;
                return true;

            case double dbl:
                result = (decimal)dbl;
                return true;

            case float f:
                result = (decimal)f;
                return true;

            case string s when decimal.TryParse(s, out var p):
                result = p;
                return true;

            default: return false;
        }
    }

    static bool TryGetDateTime(object value, out DateTime result) {
        result = default;
        switch (value) {
            case DateTime dt:
                result = dt;
                return true;

            case DateTimeOffset dto:
                result = dto.UtcDateTime;
                return true;

            case string s when DateTime.TryParse(s, out var p):
                result = p;
                return true;

            default: return false;
        }
    }
}

/// <summary>
/// Converts SQL LIKE/ILIKE patterns to compiled <see cref="Regex"/> instances.
/// <c>%</c> maps to <c>.*</c>, <c>_</c> maps to <c>.</c>, all other characters are escaped.
/// </summary>
file static class PatternMatcher {
    /// <summary>
    /// Converts a SQL LIKE pattern to an anchored regex. The resulting regex is compiled
    /// and uses <see cref="RegexOptions.Singleline"/>. Case-insensitive matching adds
    /// <see cref="RegexOptions.IgnoreCase"/>.
    /// </summary>
    // public static Regex ConvertLikeToRegex(string pattern, bool caseInsensitive) {
    //     var result = new System.Text.StringBuilder(pattern.Length + 4);
    //     result.Append('^');
    //
    //     foreach (var ch in pattern) {
    //         if (ch == '%')
    //             result.Append(".*");
    //         else if (ch == '_')
    //             result.Append('.');
    //         else if (IsRegexMetaChar(ch)) {
    //             result.Append('\\');
    //             result.Append(ch);
    //         }
    //         else
    //             result.Append(ch);
    //     }
    //
    //     result.Append('$');
    //
    //     var options = RegexOptions.Compiled | RegexOptions.Singleline;
    //     if (caseInsensitive)
    //         options |= RegexOptions.IgnoreCase;
    //
    //     return new Regex(result.ToString(), options);
    //
    //     static bool IsRegexMetaChar(char ch) =>
    //         ch is '\\' or '*' or '+' or '?' or '|' or '{' or '}' or '[' or ']' or '(' or ')' or '^' or '$' or '.';
    // }

}

#endregion
