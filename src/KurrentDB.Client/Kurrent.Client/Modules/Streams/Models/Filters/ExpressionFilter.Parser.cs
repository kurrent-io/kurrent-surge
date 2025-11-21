// ReSharper disable InconsistentNaming

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

    /// <summary>
    /// Analyzes a filter expression to extract server-side and client-side components.
    /// </summary>
    internal static FilterAnalysisResult Analyze(IFilterExpression expression) =>
        ExpressionFilterAnalyzer.Analyze(expression);
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

#region . Analyzer .

/// <summary>
/// Result of analyzing a filter expression for server/client splitting.
/// </summary>
sealed record FilterAnalysisResult {
    /// <summary>
    /// The scope of the server-side filter (StreamName or SchemaName).
    /// Null if no server-side filtering is possible.
    /// </summary>
    public FilterScope? ServerScope { get; init; }

    /// <summary>
    /// The regex pattern to send to the server.
    /// Null if no server-side filtering is possible.
    /// </summary>
    public string? ServerRegex { get; init; }

    /// <summary>
    /// The target stream if a single stream was detected (e.g., stream = 'orders').
    /// </summary>
    public StreamName? TargetStream { get; init; }

    /// <summary>
    /// The remaining expression to evaluate client-side.
    /// Null if everything was lifted to server.
    /// </summary>
    public IFilterExpression? ClientExpression { get; init; }

    /// <summary>
    /// Whether a server-side filter was extracted.
    /// </summary>
    public bool HasServerFilter => ServerRegex is not null;

    /// <summary>
    /// Whether client-side filtering is needed.
    /// </summary>
    public bool HasClientFilter => ClientExpression is not null;
}

file static class ExpressionFilterAnalyzer {
    public static FilterAnalysisResult Analyze(IFilterExpression expression) {
        var          serverPredicates = new List<IFilterExpression>();
        var          clientPredicates = new List<IFilterExpression>();

        FilterScope? serverScope      = null;
        StreamName?  targetStream     = null;

        // Flatten AND expressions into a list of conjuncts
        var conjuncts = FlattenAnd(expression);

        foreach (var conjunct in conjuncts) {
            var extraction = TryExtractServerPredicate(conjunct);

            if (extraction.HasValue) {
                var (scope, regex, stream) = extraction.Value;

                // Can only have one server filter scope
                if (serverScope is null || serverScope == scope) {
                    serverScope = scope;
                    serverPredicates.Add(conjunct);

                    if (stream is not null)
                        targetStream = stream;
                }
                else
                    // Different scope, must be client-side
                    clientPredicates.Add(conjunct);
            }
            else
                clientPredicates.Add(conjunct);
        }

        // Build server regex from all server predicates
        string? serverRegex = null;
        if (serverPredicates.Count > 0) {
            var patterns = serverPredicates
                .Select(ExtractRegexPattern)
                .Where(p => p is not null)
                .ToList();

            serverRegex = patterns.Count switch {
                1   => patterns[0],
                > 1 => $"({string.Join("|", patterns)})", // Multiple patterns for same scope - combine with |
                _   => serverRegex
            };
        }

        // Build client expression from remaining predicates
        IFilterExpression? clientExpr = null;
        if (clientPredicates.Count == 1)
            clientExpr = clientPredicates[0];
        else if (clientPredicates.Count > 1)
            clientExpr = clientPredicates.Aggregate((a, b) => new AndExpression(a, b));

        return new FilterAnalysisResult {
            ServerScope      = serverScope,
            ServerRegex      = serverRegex,
            TargetStream     = targetStream,
            ClientExpression = clientExpr
        };
    }

    static List<IFilterExpression> FlattenAnd(IFilterExpression expr) {
        var result = new List<IFilterExpression>();
        FlattenAndRecursive(expr, result);
        return result;
    }

    static void FlattenAndRecursive(IFilterExpression expr, List<IFilterExpression> result) {
        if (expr is AndExpression and) {
            FlattenAndRecursive(and.Left, result);
            FlattenAndRecursive(and.Right, result);
        }
        else
            result.Add(expr);
    }

    static (FilterScope scope, string regex, StreamName? stream)? TryExtractServerPredicate(IFilterExpression expr) {
        return expr switch {
            ComparisonExpression comp => TryExtractComparison(comp),
            LikeExpression like       => TryExtractLike(like),
            InExpression inExpr       => TryExtractIn(inExpr),
            _                         => null
        };
    }

    static (FilterScope, string, StreamName?)? TryExtractComparison(ComparisonExpression comp) {
        var propName = comp.Property.PropertyName.ToLowerInvariant();
        if (comp.Op != ComparisonOperator.Equal) return null;
        if (comp.Value is not StringValue sv) return null;

        return propName switch {
            "stream"                  => (FilterScope.StreamName, $"^{Regex.Escape(sv.Value)}$", StreamName.From(sv.Value)),
            "schema_name" or "schema" => (FilterScope.SchemaName, $"^{Regex.Escape(sv.Value)}$", null),
            _                         => null
        };
    }

    static (FilterScope, string, StreamName?)? TryExtractLike(LikeExpression like) {
        if (like.Negated) return null;

        var propName = like.Property.PropertyName.ToLowerInvariant();
        var scope = propName switch {
            "stream"                  => FilterScope.StreamName,
            "schema_name" or "schema" => FilterScope.SchemaName,
            _                         => (FilterScope?)null
        };

        if (scope is null) return null;

        // Convert LIKE pattern to regex
        var regex = ConvertLikePatternToRegex(like.Pattern);
        return (scope.Value, regex, null);
    }

    static (FilterScope, string, StreamName?)? TryExtractIn(InExpression inExpr) {
        if (inExpr.Negated) return null;

        var propName = inExpr.Property.PropertyName.ToLowerInvariant();
        var scope = propName switch {
            "stream"                  => FilterScope.StreamName,
            "schema_name" or "schema" => FilterScope.SchemaName,
            _                         => (FilterScope?)null
        };

        if (scope is null) return null;

        // All values must be strings
        var strings = new List<string>();
        foreach (var v in inExpr.Values) {
            if (v is not StringValue sv) return null;

            strings.Add(sv.Value);
        }

        // Create alternation regex
        var escaped = strings.Select(Regex.Escape);
        var regex   = $"^({string.Join("|", escaped)})$";
        return (scope.Value, regex, null);
    }

    static string? ExtractRegexPattern(IFilterExpression expr) {
        return expr switch {
            ComparisonExpression comp when comp.Op == ComparisonOperator.Equal && comp.Value is StringValue sv =>
                $"^{Regex.Escape(sv.Value)}$",
            LikeExpression like when !like.Negated =>
                ConvertLikePatternToRegex(like.Pattern),
            InExpression inExpr when !inExpr.Negated =>
                $"^({string.Join("|", inExpr.Values.OfType<StringValue>().Select(v => Regex.Escape(v.Value)))})$",
            _ => null
        };
    }

    static string ConvertLikePatternToRegex(string pattern) {
        var result = new System.Text.StringBuilder();

        for (var i = 0; i < pattern.Length; i++) {
            var ch = pattern[i];
            if (ch == '%')
                result.Append(".*");
            else if (ch == '_')
                result.Append('.');
            else
                result.Append(Regex.Escape(ch.ToString()));
        }

        return $"^{result}$";
    }
}

/// <summary>
/// Specifies what the filter targets for analysis purposes.
/// </summary>
enum FilterScope {
    Unspecified = 0,
    StreamName  = 1,
    SchemaName  = 2
}

#endregion

#region . Tokenizer .

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

file readonly record struct Token(TokenType Type, string Value, int Position) {
    public static Token EndOfInput(int position)            => new(TokenType.EndOfInput, "", position);
    public static Token Error(string message, int position) => new(TokenType.Error, message, position);
}

file sealed class FilterTokenizer(ReadOnlySpan<char> input) {
    readonly string _input = input.ToString();

    int _position;

    char Current => _position < _input.Length ? _input[_position] : '\0';
    bool IsAtEnd => _position >= _input.Length;

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
        Advance();

        var value = new System.Text.StringBuilder();
        while (!IsAtEnd && Current != '\'') {
            if (Current == '\\' && _position + 1 < _input.Length) {
                Advance();
                value.Append(Current);
            }
            else
                value.Append(Current);

            Advance();
        }

        if (IsAtEnd) return Token.Error("Unterminated string literal", start);

        Advance();
        return new Token(TokenType.String, value.ToString(), start);
    }

    Token ReadNumber() {
        var start = _position;
        if (Current == '-') Advance();

        while (!IsAtEnd && char.IsDigit(Current)) Advance();

        if (!IsAtEnd && Current == '.' && _position + 1 < _input.Length && char.IsDigit(_input[_position + 1])) {
            Advance();
            while (!IsAtEnd && char.IsDigit(Current)) Advance();
        }

        return new Token(TokenType.Number, _input[start.._position], start);
    }

    Token ReadIdentifierOrKeyword() {
        var start = _position;
        while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_')) Advance();
        var value = _input[start.._position];
        return new Token(GetKeywordType(value), value, start);
    }

    static TokenType GetKeywordType(string value) =>
        value.ToUpperInvariant() switch {
            "AND"     => TokenType.And,
            "OR"      => TokenType.Or,
            "NOT"     => TokenType.Not,
            "LIKE"    => TokenType.Like,
            "ILIKE"   => TokenType.ILike,
            "IN"      => TokenType.In,
            "BETWEEN" => TokenType.Between,
            "IS"      => TokenType.Is,
            "NULL"    => TokenType.Null,
            "TRUE"    => TokenType.True,
            "FALSE"   => TokenType.False,
            _         => TokenType.Identifier
        };

    void SkipWhitespace() {
        while (!IsAtEnd && char.IsWhiteSpace(Current)) Advance();
    }

    void Advance() => _position++;
}

#endregion

#region . Pratt Parser .

file sealed class ParseException(string nearToken, int position) : Exception {
    public string NearToken { get; } = nearToken;
    public int    Position  { get; } = position;
}

file sealed class PrattParser(List<Token> tokens) {
    int _current;

    public IFilterExpression Parse() {
        var result = ParseExpression(0);

        if (Peek().Type != TokenType.EndOfInput) {
            var token = Peek();
            throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
        }

        return result;
    }

    IFilterExpression ParseExpression(int minBindingPower) {
        var left = ParsePrefix();

        while (true) {
            var token = Peek();
            var bp    = GetInfixBindingPower(token.Type);
            if (bp <= minBindingPower) break;

            left = ParseInfix(left, token);
        }

        return left;
    }

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

    IFilterExpression ParsePropertyExpression() {
        var property = ParsePropertyAccess();
        var token    = Peek();

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
            Advance();
            var keyToken = Expect(TokenType.Identifier, "property key");
            key = keyToken.Value;
        }

        return new PropertyAccess(name, key);
    }

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

    IFilterExpression ParseLikeExpression(PropertyAccess property, bool caseInsensitive, bool negated) {
        var pattern = Expect(TokenType.String, "pattern string");
        return new LikeExpression(
            property, pattern.Value, caseInsensitive,
            negated
        );
    }

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

    IFilterExpression ParseBetweenExpression(PropertyAccess property, bool negated) {
        var lower = ParseValue();
        Expect(TokenType.And, "AND");
        var upper = ParseValue();
        return new BetweenExpression(
            property, lower, upper,
            negated
        );
    }

    FilterValue ParseValue() {
        var token = Peek();

        switch (token.Type) {
            case TokenType.String:
                Advance();
                return new StringValue(token.Value);

            case TokenType.Number:
                Advance();
                return new NumberValue(decimal.Parse(token.Value, System.Globalization.CultureInfo.InvariantCulture));

            case TokenType.True:
                Advance();
                return new BooleanValue(true);

            case TokenType.False:
                Advance();
                return new BooleanValue(false);

            case TokenType.Null:
                Advance();
                return NullValue.Instance;

            default:
                throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
        }
    }

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
        if (token.Type != type)
            throw new ParseException(token.Value.Length > 0 ? token.Value : "end of input", token.Position);

        return Advance();
    }
}

#endregion

#region . AST Nodes .

file abstract record FilterValue {
    public abstract object? GetValue();
}

file sealed record StringValue(string Value) : FilterValue {
    public override object GetValue() => Value;
}

file sealed record NumberValue(decimal Value) : FilterValue {
    public override object GetValue() => Value;
}

file sealed record BooleanValue(bool Value) : FilterValue {
    public override object GetValue() => Value;
}

file sealed record NullValue : FilterValue {
    public static readonly NullValue Instance = new();
    public override        object?   GetValue() => null;
}

file sealed record PropertyAccess(string PropertyName, string? Key = null) {
    public object? Resolve(Record record) =>
        PropertyName.ToLowerInvariant() switch {
            "stream"                        => record.Stream.Value,
            "schema_name" or "schema"       => record.Schema.SchemaName.Value,
            "timestamp" or "ts"             => record.Timestamp,
            "position" or "pos"             => record.Position.Value,
            "stream_revision" or "revision" => (long)record.StreamRevision,
            "record_id" or "id"             => record.Id,
            "properties" or "props" => Key is null
                ? null
                : record.Metadata.TryGet<object>(Key, out var value)
                    ? value
                    : null,
            _ => null
        };
}

file enum ComparisonOperator {
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

file sealed class ComparisonExpression(PropertyAccess property, ComparisonOperator op, FilterValue value) : IFilterExpression {
    public PropertyAccess     Property => property;
    public ComparisonOperator Op       => op;
    public FilterValue        Value    => value;

    public bool IsMatch(Record record) {
        var left  = property.Resolve(record);
        var right = value.GetValue();
        if (left is null || right is null) return false;

        var cmp = ValueComparer.Compare(left, right);
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
}

file sealed class AndExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression {
    public IFilterExpression Left  => left;
    public IFilterExpression Right => right;

    public bool IsMatch(Record record) => left.IsMatch(record) && right.IsMatch(record);
}

file sealed class OrExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression {
    public bool IsMatch(Record record) => left.IsMatch(record) || right.IsMatch(record);
}

file sealed class NotExpression(IFilterExpression inner) : IFilterExpression {
    public bool IsMatch(Record record) => !inner.IsMatch(record);
}

file sealed class LikeExpression(PropertyAccess property, string pattern, bool caseInsensitive, bool negated) : IFilterExpression {
    readonly Regex          _regex = PatternMatcher.ConvertLikeToRegex(pattern, caseInsensitive);
    public   PropertyAccess Property => property;
    public   string         Pattern  => pattern;
    public   bool           Negated  => negated;

    public bool IsMatch(Record record) {
        var value = property.Resolve(record);
        if (value is null) return false;

        var matches = _regex.IsMatch(value.ToString() ?? "");
        return negated ? !matches : matches;
    }
}

file sealed class IsNullExpression(PropertyAccess property, bool negated) : IFilterExpression {
    public bool IsMatch(Record record) {
        var isNull = property.Resolve(record) is null;
        return negated ? !isNull : isNull;
    }
}

file sealed class InExpression(PropertyAccess property, IReadOnlyList<FilterValue> values, bool negated) : IFilterExpression {
    public PropertyAccess             Property => property;
    public IReadOnlyList<FilterValue> Values   => values;
    public bool                       Negated  => negated;

    public bool IsMatch(Record record) {
        var propValue = property.Resolve(record);
        if (propValue is null) return false;

        var found = values.Any(v => ValueComparer.Compare(propValue, v.GetValue()) == 0);
        return negated ? !found : found;
    }
}

file sealed class BetweenExpression(PropertyAccess property, FilterValue lower, FilterValue upper, bool negated) : IFilterExpression {
    public bool IsMatch(Record record) {
        var value = property.Resolve(record);
        if (value is null) return false;

        var inRange = ValueComparer.Compare(value, lower.GetValue()) >= 0 &&
                      ValueComparer.Compare(value, upper.GetValue()) <= 0;

        return negated ? !inRange : inRange;
    }
}

#endregion

#region . Utilities .

file static class ValueComparer {
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

file static class PatternMatcher {
    public static Regex ConvertLikeToRegex(string pattern, bool caseInsensitive) {
        var result = new System.Text.StringBuilder();

        foreach (var ch in pattern)
            if (ch == '%')
                result.Append(".*");
            else if (ch == '_')
                result.Append('.');
            else
                result.Append(Regex.Escape(ch.ToString()));

        var options                  = RegexOptions.Compiled | RegexOptions.Singleline;
        if (caseInsensitive) options |= RegexOptions.IgnoreCase;

        return new Regex($"^{result}$", options);
    }
}

#endregion
