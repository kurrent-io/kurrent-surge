using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Turns filter text into an expression tree. A Pratt parser: OR binds at 10, AND at 20, NOT at 60.
/// Comparison, LIKE, IN, BETWEEN and IS NULL are parsed as prefix predicates on a column reference.
/// </summary>
static class FilterParser {
    public static bool TryParse(
        ReadOnlySpan<char> input,
        [NotNullWhen(true)]  out IFilterExpression? expression,
        [NotNullWhen(false)] out FilterParseError? error
    ) {
        expression = null;
        error      = null;

        if (input.IsEmpty || input.IsWhiteSpace()) {
            error = FilterParseError.EmptyInput();
            return false;
        }

        var tokens = new FilterTokenizer(input).Tokenize();

        var errorToken = tokens.FirstOrDefault(t => t.Type == TokenType.Error);
        if (errorToken.Type == TokenType.Error) {
            error = FilterParseError.SyntaxError(errorToken.Value, errorToken.Position);
            return false;
        }

        try {
            expression = new PrattParser(tokens).Parse();
            return true;
        }
        catch (ParseException ex) {
            error = FilterParseError.SyntaxError(ex.NearToken, ex.Position);
            return false;
        }
    }
}

/// <summary>
/// Signals an unexpected token. Caught at the parser boundary and turned into a
/// <see cref="FilterParseError"/>; it never escapes.
/// </summary>
sealed class ParseException(string nearToken, int position) : Exception {
    public string NearToken { get; } = nearToken;
    public int    Position  { get; } = position;
}

sealed class PrattParser(List<Token> tokens) {
    int _current;

    public IFilterExpression Parse() {
        var result = ParseExpression(0);

        if (Peek().Type == TokenType.EndOfInput) return result;

        throw Unexpected(Peek());
    }

    IFilterExpression ParseExpression(int minBindingPower) {
        var left = ParsePrefix();

        while (BindingPower(Peek().Type) > minBindingPower)
            left = ParseInfix(left, Peek());

        return left;
    }

    IFilterExpression ParsePrefix() {
        var token = Peek();

        switch (token.Type) {
            case TokenType.LeftParen:
                Advance();
                var grouped = ParseExpression(0);
                Expect(TokenType.RightParen);
                return grouped;

            case TokenType.Not:
                Advance();
                if (Peek().Type != TokenType.LeftParen)
                    return new NotExpression(ParseExpression(60));

                Advance();
                var inner = ParseExpression(0);
                Expect(TokenType.RightParen);
                return new NotExpression(inner);

            case TokenType.Identifier:
                return ParsePredicate();

            default:
                throw Unexpected(token);
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
                throw Unexpected(token);
        }
    }

    IFilterExpression ParsePredicate() {
        var property = ParsePropertyAccess();
        var token    = Peek();

        switch (token.Type) {
            case TokenType.Is:
                return ParseIsNull(property);

            case TokenType.In:
                Advance();
                return ParseIn(property, negated: false);

            case TokenType.Not:
                return ParseNegated(property);

            case TokenType.Like:
                Advance();
                return ParseLike(property, caseInsensitive: false, negated: false);

            case TokenType.ILike:
                Advance();
                return ParseLike(property, caseInsensitive: true, negated: false);

            case TokenType.Between:
                Advance();
                return ParseBetween(property, negated: false);

            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.GreaterThan:
            case TokenType.LessThan:
            case TokenType.GreaterOrEqual:
            case TokenType.LessOrEqual:
                return ParseComparison(property);

            default:
                throw Unexpected(token);
        }
    }

    /// <summary>
    /// Reads a column reference. Dot segments are joined into the name and left for the schema to
    /// split, because only the schema knows which prefix is a declared column. A bracket or arrow
    /// key is separated here, since the syntax already says where the key starts.
    /// </summary>
    PropertyAccess ParsePropertyAccess() {
        var     name = Expect(TokenType.Identifier).Value;
        string? key  = null;

        if (Peek().Type == TokenType.Dot) {
            var segments = new List<string> { name };

            while (Peek().Type == TokenType.Dot) {
                Advance();
                segments.Add(Expect(TokenType.Identifier).Value);
            }

            name = string.Join(".", segments);
        }

        switch (Peek().Type) {
            case TokenType.LeftBracket:
                Advance();
                key = Expect(TokenType.String).Value;
                Expect(TokenType.RightBracket);
                break;

            case TokenType.ArrowText:
                Advance();
                key = Expect(TokenType.String).Value;
                break;
        }

        return new(name, key);
    }

    IFilterExpression ParseIsNull(PropertyAccess property) {
        Advance();

        var negated = Peek().Type == TokenType.Not;
        if (negated) Advance();

        Expect(TokenType.Null);
        return new IsNullExpression(property, negated);
    }

    IFilterExpression ParseNegated(PropertyAccess property) {
        Advance();

        var token = Peek();
        switch (token.Type) {
            case TokenType.In:
                Advance();
                return ParseIn(property, negated: true);

            case TokenType.Like:
                Advance();
                return ParseLike(property, caseInsensitive: false, negated: true);

            case TokenType.ILike:
                Advance();
                return ParseLike(property, caseInsensitive: true, negated: true);

            case TokenType.Between:
                Advance();
                return ParseBetween(property, negated: true);

            default:
                throw Unexpected(token);
        }
    }

    IFilterExpression ParseComparison(PropertyAccess property) {
        var token = Advance();

        var op = token.Type switch {
            TokenType.Equal          => ComparisonOperator.Equal,
            TokenType.NotEqual       => ComparisonOperator.NotEqual,
            TokenType.GreaterThan    => ComparisonOperator.GreaterThan,
            TokenType.LessThan       => ComparisonOperator.LessThan,
            TokenType.GreaterOrEqual => ComparisonOperator.GreaterThanOrEqual,
            TokenType.LessOrEqual    => ComparisonOperator.LessThanOrEqual,
            _                        => throw Unexpected(token)
        };

        return new ComparisonExpression(property, op, ParseValue());
    }

    IFilterExpression ParseLike(PropertyAccess property, bool caseInsensitive, bool negated) =>
        new LikeExpression(property, Expect(TokenType.String).Value, caseInsensitive, negated);

    IFilterExpression ParseIn(PropertyAccess property, bool negated) {
        Expect(TokenType.LeftParen);

        if (Peek().Type == TokenType.RightParen)
            throw Unexpected(Peek());

        var values = new List<FilterValue> { ParseValue() };

        while (Peek().Type == TokenType.Comma) {
            Advance();
            values.Add(ParseValue());
        }

        Expect(TokenType.RightParen);
        return new InExpression(property, values, negated);
    }

    IFilterExpression ParseBetween(PropertyAccess property, bool negated) {
        var lower = ParseValue();
        Expect(TokenType.And);
        var upper = ParseValue();

        return new BetweenExpression(property, lower, upper, negated);
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
                throw Unexpected(token);
        }
    }

    static int BindingPower(TokenType type) =>
        type switch {
            TokenType.Or  => 10,
            TokenType.And => 20,
            _             => 0
        };

    Token Peek()    => _current < tokens.Count ? tokens[_current] : Token.EndOfInput(0);
    Token Advance() => tokens[_current++];

    Token Expect(TokenType type) {
        var token = Peek();
        return token.Type != type ? throw Unexpected(token) : Advance();
    }

    static ParseException Unexpected(Token token) =>
        new(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
}
