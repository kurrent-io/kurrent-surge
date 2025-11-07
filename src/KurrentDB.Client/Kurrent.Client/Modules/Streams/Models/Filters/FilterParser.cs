using System.Globalization;

namespace Kurrent.Client.Streams;

static class FilterParser {
    public static ParseResult TryParse(ReadOnlySpan<char> input) {
        var tokenizer = new Tokenizer(input);
        var tokens    = new List<Token>();

        while (tokenizer.NextToken() is { } token)
            if (token.Type != TokenType.Whitespace)
                tokens.Add(token);

        if (tokens.Count == 0)
            return ParseResult.Empty;

        var parser = new Parser(tokens);
        try {
            var expression = parser.ParseExpression();
            return new ParseResult(expression, null);
        }
        catch (Exception ex) {
            return new ParseResult(null, ex.Message);
        }
    }
}

readonly record struct ParseResult(IFilterExpression? Expression, string? Error) {
    public static readonly ParseResult Empty = new(null, null);

    public bool IsSuccess => Expression != null && Error == null;
    public bool IsEmpty   => Expression == null && Error == null;
}

enum TokenType {
    // Values and identifiers
    Identifier,
    StringLiteral,
    NumberLiteral,
    DateTimeLiteral,
    
    // Comparison operators
    Equals,
    NotEquals,
    NotEqualsAlt,  // <>
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    
    // PostgreSQL regex operators
    Tilde,            // ~
    TildeAsterisk,    // ~*
    NotTilde,         // !~
    NotTildeAsterisk, // !~*
    
    // Logical operators and keywords
    And,
    Or,
    Is,
    Null,
    Not,
    In,
    
    // Punctuation
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    
    // Other
    Whitespace,
    EndOfInput,
    End  // For compatibility with some tests
}

readonly record struct Token(TokenType Type, ReadOnlyMemory<char> Value, int Position);

ref struct Tokenizer {
    readonly ReadOnlySpan<char> _input;
    int                         _position;

    public Tokenizer(ReadOnlySpan<char> input) => _input = input;

    public Token? NextToken() {
        if (_position >= _input.Length)
            return new Token(TokenType.EndOfInput, ReadOnlyMemory<char>.Empty, _position);

        var start = _position;
        var ch    = _input[_position];

        var result = ch switch {
            '(' => new Token(TokenType.LeftParen, _input.Slice(_position, 1).ToArray(), _position),
            ')' => new Token(TokenType.RightParen, _input.Slice(_position, 1).ToArray(), _position),
            '[' => new Token(TokenType.LeftBracket, _input.Slice(_position, 1).ToArray(), _position),
            ']' => new Token(TokenType.RightBracket, _input.Slice(_position, 1).ToArray(), _position),
            ',' => new Token(TokenType.Comma, _input.Slice(_position, 1).ToArray(), _position),
            '\'' or '"' => ReadString(),
            var c when char.IsWhiteSpace(c) => ReadWhitespace(),
            var c when char.IsLetter(c) || c == '_' || c == '@' || c == '$' => ReadIdentifier(),
            var c when char.IsDigit(c) => ReadNumber(),
            _ => ReadOperator()
        };

        // Advance position for single-character tokens
        if (result.Type is TokenType.LeftParen or TokenType.RightParen or TokenType.LeftBracket or TokenType.RightBracket or TokenType.Comma)
            _position++;

        return result;
    }

    Token ReadString() {
        var start     = _position;
        var quote     = _input[_position++];
        var hasEscape = false;

        while (_position < _input.Length) {
            var ch = _input[_position];
            if (ch == quote) {
                _position++;
                break;
            }

            if (ch == '\\') {
                hasEscape = true;
                _position++;
                if (_position < _input.Length) _position++;
            }
            else {
                _position++;
            }
        }

        var valueSpan = _input.Slice(start + 1, _position - start - 2);
        var value     = hasEscape ? UnescapeString(valueSpan) : valueSpan.ToArray();

        return new Token(TokenType.StringLiteral, value, start);
    }

    ReadOnlyMemory<char> UnescapeString(ReadOnlySpan<char> input) {
        var result = new char[input.Length];
        var j      = 0;

        for (var i = 0; i < input.Length; i++)
            if (input[i] == '\\' && i + 1 < input.Length) {
                var next = input[i + 1];
                result[j++] = next switch {
                    'n'  => '\n',
                    'r'  => '\r',
                    't'  => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"'  => '"',
                    _    => next
                };

                i++;
            }
            else {
                result[j++] = input[i];
            }

        return result.AsMemory(0, j);
    }

    Token ReadWhitespace() {
        var start = _position;
        while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
            _position++;

        return new Token(TokenType.Whitespace, _input.Slice(start, _position - start).ToArray(), start);
    }

    Token ReadIdentifier() {
        var start = _position;
        while (_position < _input.Length) {
            var ch = _input[_position];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '@' || ch == '$')
                _position++;
            else
                break;
        }

        var value = _input.Slice(start, _position - start);
        var tokenType = GetIdentifierTokenType(value);
        
        return new Token(tokenType, value.ToArray(), start);
    }

    static TokenType GetIdentifierTokenType(ReadOnlySpan<char> value) {
        // Case-insensitive keyword matching
        return value.ToString().ToUpperInvariant() switch {
            "AND"  => TokenType.And,
            "OR"   => TokenType.Or,
            "IS"   => TokenType.Is,
            "NULL" => TokenType.Null,
            "NOT"  => TokenType.Not,
            "IN"   => TokenType.In,
            _      => TokenType.Identifier
        };
    }

    Token ReadNumber() {
        var start = _position;
        while (_position < _input.Length && (char.IsDigit(_input[_position]) || _input[_position] == '.'))
            _position++;

        return new Token(TokenType.NumberLiteral, _input.Slice(start, _position - start).ToArray(), start);
    }

    Token ReadOperator() {
        var start = _position;

        // Check for 3-character operators first (!~*)
        if (_position + 2 < _input.Length) {
            var threeChar = _input.Slice(_position, 3);
            if (threeChar.SequenceEqual("!~*")) {
                _position += 3;
                return new Token(TokenType.NotTildeAsterisk, threeChar.ToArray(), start);
            }
        }

        // Check for 2-character operators
        if (_position + 1 < _input.Length) {
            var twoChar = _input.Slice(_position, 2);
            var result = twoChar switch {
                _ when twoChar.SequenceEqual("==") => new Token(TokenType.Equals, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("!=") => new Token(TokenType.NotEquals, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("<>") => new Token(TokenType.NotEqualsAlt, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("<=") => new Token(TokenType.LessThanOrEqual, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual(">=") => new Token(TokenType.GreaterThanOrEqual, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("&&") => new Token(TokenType.And, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("||") => new Token(TokenType.Or, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("~*") => new Token(TokenType.TildeAsterisk, twoChar.ToArray(), start),
                _ when twoChar.SequenceEqual("!~") => new Token(TokenType.NotTilde, twoChar.ToArray(), start),
                _ => (Token?)null
            };
            
            if (result.HasValue) {
                _position += 2;
                return result.Value;
            }
        }

        // Single character operators
        var ch = _input[_position];
        var singleResult = ch switch {
            '=' => new Token(TokenType.Equals, _input.Slice(_position, 1).ToArray(), start),
            '<' => new Token(TokenType.LessThan, _input.Slice(_position, 1).ToArray(), start),
            '>' => new Token(TokenType.GreaterThan, _input.Slice(_position, 1).ToArray(), start),
            '~' => new Token(TokenType.Tilde, _input.Slice(_position, 1).ToArray(), start),
            _ => throw new InvalidOperationException($"Unknown operator character: '{ch}' at position {_position}")
        };

        _position++;
        return singleResult;
    }
}

ref struct Parser {
    readonly List<Token> _tokens;
    int                  _position;

    public Parser(List<Token> tokens) => _tokens = tokens;

    Token Current => _position < _tokens.Count ? _tokens[_position] : new Token(TokenType.EndOfInput, ReadOnlyMemory<char>.Empty, -1);

    Token Peek(int offset = 1) {
        var pos = _position + offset;
        return pos < _tokens.Count ? _tokens[pos] : new Token(TokenType.EndOfInput, ReadOnlyMemory<char>.Empty, -1);
    }

    void Advance() => _position++;

    bool Match(TokenType type) => Current.Type == type;

    bool Match(string value) => Current.Value.Span.SequenceEqual(value);

    Token Consume(TokenType expected) {
        if (!Match(expected))
            throw new InvalidOperationException($"Expected {expected} but found {Current.Type} at position {Current.Position}");

        var token = Current;
        Advance();
        return token;
    }

    Token Consume(string expected) {
        if (!Match(expected))
            throw new InvalidOperationException($"Expected '{expected}' but found '{Current.Value}' at position {Current.Position}");

        var token = Current;
        Advance();
        return token;
    }

    public IFilterExpression ParseExpression() => ParseOr();

    IFilterExpression ParseOr() {
        var left = ParseAnd();

        while (Match(TokenType.Or)) {
            Advance();
            var right = ParseAnd();
            left = new LogicalExpression(LogicalOperator.Or, left, right);
        }

        return left;
    }

    IFilterExpression ParseAnd() {
        var left = ParseComparison();

        while (Match(TokenType.And)) {
            Advance();
            var right = ParseComparison();
            left = new LogicalExpression(LogicalOperator.And, left, right);
        }

        return left;
    }

    IFilterExpression ParseComparison() {
        if (Match(TokenType.LeftParen)) {
            Advance();
            var expr = ParseExpression();
            Consume(TokenType.RightParen);
            return expr;
        }

        var identifier = Consume(TokenType.Identifier).Value.ToString();

        // Handle IS NULL / IS NOT NULL
        if (Match(TokenType.Is)) {
            Advance();
            if (Match(TokenType.Not)) {
                Advance();
                Consume(TokenType.Null);
                return new PropertyNullExpression(identifier, false);
            }
            
            Consume(TokenType.Null);
            return new PropertyNullExpression(identifier, true);
        }

        // Handle NOT IN
        if (Match(TokenType.Not)) {
            Advance();
            Consume(TokenType.In);
            
            Consume(TokenType.LeftBracket);
            var values = new List<object>();
            if (!Match(TokenType.RightBracket)) {
                do {
                    values.Add(ParseValue());
                    if (!Match(TokenType.Comma)) break;
                    Advance();
                } while (!Match(TokenType.RightBracket));
            }
            Consume(TokenType.RightBracket);
            return new NotInExpression(identifier, values.ToArray());
        }

        // Handle IN
        if (Match(TokenType.In)) {
            Advance();
            Consume(TokenType.LeftBracket);
            
            var values = new List<object>();
            if (!Match(TokenType.RightBracket)) {
                do {
                    values.Add(ParseValue());
                    if (!Match(TokenType.Comma)) break;
                    Advance();
                } while (!Match(TokenType.RightBracket));
            }
            Consume(TokenType.RightBracket);
            return new PropertyInExpression(identifier, values.ToArray());
        }

        // Handle PostgreSQL regex operators
        if (Match(TokenType.Tilde)) {
            Advance();
            var pattern = ParseValue().ToString()!;
            return new ContainsExpression(identifier, pattern);
        }

        if (Match(TokenType.TildeAsterisk)) {
            Advance();
            var pattern = ParseValue().ToString()!;
            return new ContainsExactExpression(identifier, pattern);
        }

        if (Match(TokenType.NotTilde)) {
            Advance();
            var pattern = ParseValue().ToString()!;
            return new NotContainsExpression(identifier, pattern);
        }

        if (Match(TokenType.NotTildeAsterisk)) {
            Advance();
            var pattern = ParseValue().ToString()!;
            return new NotContainsExactExpression(identifier, pattern);
        }

        // Handle comparison operators
        if (Match(TokenType.Equals)) {
            Advance();
            var rightValue = ParseValue();
            return new PropertyEqualsExpression(identifier, rightValue);
        }

        if (Match(TokenType.NotEquals) || Match(TokenType.NotEqualsAlt)) {
            Advance();
            var rightValue = ParseValue();
            return new PropertyComparisonExpression(identifier, ComparisonOperator.NotEqual, rightValue);
        }

        if (Match(TokenType.GreaterThan)) {
            Advance();
            var rightValue = ParseValue();
            return new PropertyComparisonExpression(identifier, ComparisonOperator.GreaterThan, rightValue);
        }

        if (Match(TokenType.LessThan)) {
            Advance();
            var rightValue = ParseValue();
            return new PropertyComparisonExpression(identifier, ComparisonOperator.LessThan, rightValue);
        }

        if (Match(TokenType.GreaterThanOrEqual)) {
            Advance();
            var rightValue = ParseValue();
            return new PropertyComparisonExpression(identifier, ComparisonOperator.GreaterThanOrEqual, rightValue);
        }

        if (Match(TokenType.LessThanOrEqual)) {
            Advance();
            var rightValue = ParseValue();
            return new PropertyComparisonExpression(identifier, ComparisonOperator.LessThanOrEqual, rightValue);
        }

        throw new InvalidOperationException($"Unexpected token: {Current.Type} at position {Current.Position}");
    }


    object ParseValue() {
        return Current.Type switch {
            TokenType.StringLiteral   => ParseStringValue(),
            TokenType.NumberLiteral   => ParseNumericValue(),
            TokenType.DateTimeLiteral => ParseDateTimeValue(),
            TokenType.Identifier      => ParseIdentifierValue(),
            _                         => throw new InvalidOperationException($"Expected value but found {Current.Type} at position {Current.Position}")
        };
    }

    object ParseDateTimeValue() {
        var token = Current;
        Advance();
        var str = token.Value.ToString();
        
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            return dateTime;
            
        if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
            return dateTimeOffset;
            
        return str; // Fallback to string if parsing fails
    }

    object ParseStringValue() {
        var token = Current;
        Advance();
        return token.Value.ToString();
    }

    object ParseNumericValue() {
        var token = Current;
        Advance();
        var str = token.Value.ToString();

        if (str.Contains('.') && decimal.TryParse(
                str, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var d
            ))
            return d;

        if (long.TryParse(
                str, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var l
            ))
            return l;

        if (double.TryParse(
                str, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var dbl
            ))
            return dbl;

        return str;
    }

    object ParseIdentifierValue() {
        var token = Current;
        Advance();
        var str = token.Value.ToString();

        return str.ToLowerInvariant() switch {
            "true"  => true,
            "false" => false,
            "null"  => null!,
            _       => str
        };
    }
}
