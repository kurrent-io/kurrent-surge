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
    ) => TryParse(input, null, out expression, out error);

    public static bool TryParse(
        ReadOnlySpan<char> input,
        FilterParameters? parameters,
        [NotNullWhen(true)]  out IFilterExpression? expression,
        [NotNullWhen(false)] out FilterParseError? error
    ) {
        expression = null;

        if (!TryTokenize(input, out var tokens, out error)) return false;

        var used = parameters is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try {
            expression = new PrattParser(tokens, parameters, used).Parse();
        }
        catch (ParseException ex) {
            error = ex.Unbound is { } unbound
                ? FilterParseError.UnboundParameter(unbound, Bound(parameters))
                : FilterParseError.SyntaxError(ex.NearToken, ex.Position);

            return false;
        }

        // A bound value the expression never names is a typo on one side or the other, and silently
        // ignoring it would leave the filter narrower or wider than the caller believes.
        if (parameters is not null) {
            foreach (var name in parameters.Names) {
                if (used!.Contains(name)) continue;

                expression = null;
                error      = FilterParseError.UnusedParameter(name);

                return false;
            }
        }

        return true;

        static string Bound(FilterParameters? parameters) =>
            parameters is null or { Count: 0 } ? " (none are bound)" : $" (bound: {parameters})";
    }

    /// <summary>
    /// Parses a bare column reference — <c>stream</c>, <c>props['region']</c>, <c>data-&gt;&gt;'city'</c>,
    /// or a dotted path — with no predicate around it.
    /// </summary>
    public static bool TryParsePath(
        ReadOnlySpan<char> input,
        out PropertyAccess property,
        [NotNullWhen(false)] out FilterParseError? error
    ) {
        property = default;

        if (!TryTokenize(input, out var tokens, out error)) return false;

        try {
            property = new PrattParser(tokens).ParsePath();
            return true;
        }
        catch (ParseException ex) {
            error = FilterParseError.SyntaxError(ex.NearToken, ex.Position);
            return false;
        }
    }

    static bool TryTokenize(
        ReadOnlySpan<char> input,
        [NotNullWhen(true)]  out List<Token> tokens,
        [NotNullWhen(false)] out FilterParseError? error
    ) {
        tokens = [];
        error  = null;

        if (input.IsEmpty || input.IsWhiteSpace()) {
            error = FilterParseError.EmptyInput();
            return false;
        }

        tokens = new FilterTokenizer(input).Tokenize();

        var errorToken = tokens.FirstOrDefault(t => t.Type == TokenType.Error);
        if (errorToken.Type != TokenType.Error) return true;

        error = FilterParseError.SyntaxError(errorToken.Value, errorToken.Position);
        return false;
    }
}

/// <summary>
/// Signals an unexpected token. Caught at the parser boundary and turned into a
/// <see cref="FilterParseError"/>; it never escapes.
/// </summary>
sealed class ParseException(string nearToken, int position) : Exception {
    public string  NearToken { get; } = nearToken;
    public int     Position  { get; } = position;

    /// <summary>
    /// Set when the parse stopped on a parameter nothing was bound to, so the boundary can report
    /// that rather than a bare syntax error.
    /// </summary>
    public string? Unbound { get; init; }
}

sealed class PrattParser(List<Token> tokens, FilterParameters? parameters = null, HashSet<string>? used = null) {
    int _current;

    public IFilterExpression Parse() {
        var result = ParseExpression(0);

        if (Peek().Type == TokenType.EndOfInput) return result;

        throw Unexpected(Peek());
    }

    public PropertyAccess ParsePath() {
        var property = ParsePropertyAccess();

        if (Peek().Type == TokenType.EndOfInput) return property;

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
        var access = ParseCallOrColumn();

        // A postfix cast binds tighter than any predicate, and chains: `col::BLOB::VARCHAR`.
        while (Peek().Type == TokenType.DoubleColon) {
            Advance();
            access = Wrap(access, new CastTransform(Expect(TokenType.Identifier).Value));
        }

        return access;
    }

    PropertyAccess ParseCallOrColumn() {
        var identifier = Expect(TokenType.Identifier).Value;

        if (Peek().Type == TokenType.LeftParen) {
            // CAST is a special form: `AS` separates its operand from the target, where a function
            // would take a comma.
            if (IsCast(identifier, out var strict)) {
                Advance();
                var operand = ParsePropertyAccess();
                Expect(TokenType.As);
                var target = Expect(TokenType.Identifier).Value;
                Expect(TokenType.RightParen);

                return Wrap(operand, new CastTransform(target, strict));
            }

            if (!FilterFunctions.IsKnown(identifier))
                throw new ParseException(identifier, Peek().Position);

            Advance();
            var inner     = ParsePropertyAccess();
            var arguments = new List<FilterValue>();

            while (Peek().Type == TokenType.Comma) {
                Advance();
                arguments.Add(ParseValue());
            }

            Expect(TokenType.RightParen);
            return Wrap(inner, new FunctionTransform(identifier, arguments));
        }

        var     name = identifier;
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

    static PropertyAccess Wrap(PropertyAccess access, ColumnTransform transform) =>
        access with { Transforms = [.. access.Transforms, transform] };

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
        var value = ParseValueCore();

        // A cast on the literal side folds now, so both the evaluator and the renderer see a constant.
        while (Peek().Type == TokenType.DoubleColon) {
            Advance();
            value = Fold(Expect(TokenType.Identifier).Value, value, strict: true);
        }

        return value;
    }

    FilterValue ParseValueCore() {
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

            case TokenType.Parameter:
                Advance();
                return Bound(token);

            case TokenType.Identifier:
                return ParseTypedLiteralOrCall(token);

            default:
                throw Unexpected(token);
        }
    }

    /// <summary>
    /// A literal that names its type, or a call over literals. Both reduce to a constant here, so
    /// nothing downstream has to evaluate anything that does not depend on a record.
    /// </summary>
    FilterValue ParseTypedLiteralOrCall(Token token) {
        var identifier = token.Value;

        // TIMESTAMP '2024-01-01' — a type name followed by its text form.
        if (Peek(1).Type == TokenType.String && FilterFunctions.IsCastTarget(identifier)) {
            Advance();
            var text = Advance().Value;

            return Fold(identifier, new StringFilterValue(text), strict: true);
        }

        // CAST over a literal folds now. A constant that cannot convert can never convert, so the
        // failure belongs at parse time rather than once per record.
        if (Peek(1).Type == TokenType.LeftParen && IsCast(identifier, out var strict)) {
            Advance();
            Advance();

            var operand = ParseValue();
            Expect(TokenType.As);
            var target = Expect(TokenType.Identifier).Value;
            Expect(TokenType.RightParen);

            return Fold(target, operand, strict);
        }

        if (Peek(1).Type != TokenType.LeftParen || !FilterFunctions.IsKnown(identifier))
            throw Unexpected(token);

        Advance();
        Advance();

        var arguments = new List<FilterValue> { ParseValue() };

        while (Peek().Type == TokenType.Comma) {
            Advance();
            arguments.Add(ParseValue());
        }

        Expect(TokenType.RightParen);

        var extra  = arguments.Skip(1).Select(a => a.GetValue()).ToArray();
        var folded = FilterFunctions.Apply(identifier, arguments[0].GetValue(), extra);

        return ToFilterValue(folded, identifier, token.Position);
    }

    static bool IsCast(string identifier, out bool strict) {
        strict = identifier.Equals("CAST", StringComparison.OrdinalIgnoreCase);

        return strict || identifier.Equals("TRY_CAST", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves <c>@name</c> to the value bound to it. The value becomes a literal node here, so it is
    /// never spliced into the expression as text.
    /// </summary>
    FilterValue Bound(Token token) {
        if (parameters is null || !parameters.TryGet(token.Value, out var value))
            throw new ParseException($"@{token.Value}", token.Position) { Unbound = token.Value };

        used?.Add(token.Value);
        return value;
    }

    FilterValue Fold(string target, FilterValue value, bool strict) {
        if (!FilterFunctions.IsCastTarget(target))
            throw new ParseException(target, Peek().Position);

        if (!FilterFunctions.TryCast(value.GetValue(), target, out var result))
            return strict ? throw new ParseException($"cannot cast to {target}", Peek().Position) : NullFilterValue.Instance;

        return ToFilterValue(result, target, Peek().Position);
    }

    static FilterValue ToFilterValue(object? value, string source, int position) =>
        value switch {
            null              => NullFilterValue.Instance,
            string text       => new StringFilterValue(text),
            bool flag         => new BooleanFilterValue(flag),
            long integral     => new NumberFilterValue(integral),
            int integral      => new NumberFilterValue(integral),
            decimal number    => new NumberFilterValue(number),
            double number     => new NumberFilterValue((decimal)number),
            byte[] bytes      => new BlobFilterValue(bytes),
            Guid guid         => new StringFilterValue(guid.ToString("D")),
            DateTime date     => new DateTimeFilterValue(date, source),
            _                 => throw new ParseException(source, position)
        };

    static int BindingPower(TokenType type) =>
        type switch {
            TokenType.Or  => 10,
            TokenType.And => 20,
            _             => 0
        };

    Token Peek()              => Peek(0);
    Token Peek(int offset)    => _current + offset < tokens.Count ? tokens[_current + offset] : Token.EndOfInput(0);
    Token Advance()           => tokens[_current++];

    Token Expect(TokenType type) {
        var token = Peek();
        return token.Type != type ? throw Unexpected(token) : Advance();
    }

    static ParseException Unexpected(Token token) =>
        new(token.Value.Length > 0 ? token.Value : "end of input", token.Position);
}
