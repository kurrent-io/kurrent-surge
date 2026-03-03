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

    IndexExpression ParseIndexExpression() {
        Expect(TokenType.Equal, "=");
        var name = Expect(TokenType.String, "index name");
        return new IndexExpression(name.Value);
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
            var segments = new List<string> { name };
            while (Peek().Type == TokenType.Dot) {
                Advance();
                var segment = Expect(TokenType.Identifier, "property segment");
                segments.Add(segment.Value);
            }

            // find the property map segment (properties, props, metadata) to split name/key
            var mapIndex = segments.FindIndex(s =>
                s.Equals("properties", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("props", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("metadata", StringComparison.OrdinalIgnoreCase)
            );

            if (mapIndex >= 0 && mapIndex < segments.Count - 1) {
                // everything up to and including the map segment is the name
                name = string.Join(".", segments[..(mapIndex + 1)]);
                key  = string.Join(".", segments[(mapIndex + 1)..]);
            } else {
                name = string.Join(".", segments);
            }
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

public abstract record FilterValue {
    public abstract object? GetValue();
}

public sealed record StringFilterValue(string Value) : FilterValue {
    public override object GetValue() => Value;
}

sealed record NumberFilterValue(decimal Value) : FilterValue {
    public override object GetValue() => Value;
}

sealed record BooleanFilterValue(bool Value) : FilterValue {
    public override object GetValue() => Value;
}

sealed record NullFilterValue : FilterValue {
    public static readonly NullFilterValue Instance = new();

    public override object? GetValue() => null;
}

public sealed record PropertyAccess(string PropertyName, string? Key = null) {
    public object? Resolve(Record record) =>
        PropertyName.ToLowerInvariant() switch {
            "stream" or "stream_name"                          => record.Stream.Value,
            "schema_name" or "schema" or "event_type"          => record.Schema.SchemaName.Value,
            "timestamp" or "ts" or "created_at" or "created_ts"
                                                               => record.Timestamp,
            "position" or "pos" or "log_position"              => record.Position.LogPosition.Value,
            "category"                                         => DeriveCategory(record.Stream.Value),
            "stream_revision" or "revision" or "event_number"  => (long)record.Revision,
            "record_id" or "id"                                => record.Id,
            "properties" or "props" or "metadata" => Key is null
                ? null
                : record.Metadata.TryGetValue<object>(Key, out var value)
                    ? value
                    : null,
            "rec.stream" or "rec.position.stream"            => record.Stream.Value,
            "rec.category"                                   => DeriveCategory(record.Stream.Value),
            "rec.schema.name"                                => record.Schema.SchemaName.Value,
            "rec.revision" or "rec.position.streamrevision"  => (long)record.Revision,
            "rec.position.logposition"                       => record.Position.LogPosition.Value,
            "rec.timestamp"                                  => record.Timestamp,
            "rec.id"                                         => record.Id,
            "rec.properties" => Key is null
                ? null
                : record.Metadata.TryGetValue<object>(Key, out var v)
                    ? v
                    : null,
            _ => null
        };

    static string DeriveCategory(string stream) {
        var idx = stream.IndexOf('-');
        return idx < 0 ? stream : stream[..idx];
    }
}

public enum ComparisonOperator {
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

public sealed class ComparisonExpression(PropertyAccess property, ComparisonOperator op, FilterValue value) : IFilterExpression {
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

public sealed class AndExpression(IFilterExpression left, IFilterExpression right) : IFilterExpression {
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

public sealed class LikeExpression(PropertyAccess property, string pattern, bool caseInsensitive, bool negated) : IFilterExpression {
    readonly Regex          _regex = PatternMatcher.ConvertLikeToRegex(pattern, caseInsensitive);

    public PropertyAccess Property        => property;
    public string         Pattern         => pattern;
    public bool           CaseInsensitive => caseInsensitive;
    public bool           Negated         => negated;

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

public sealed class InExpression(PropertyAccess property, IReadOnlyList<FilterValue> values, bool negated) : IFilterExpression {
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

public sealed record IndexExpression(string IndexName) : IFilterExpression {
    public bool IsMatch(Record record) => true;
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

        var options = RegexOptions.Compiled | RegexOptions.Singleline;
        if (caseInsensitive)
            options |= RegexOptions.IgnoreCase;

        return new Regex($"^{result}$", options);
    }
}

#endregion
