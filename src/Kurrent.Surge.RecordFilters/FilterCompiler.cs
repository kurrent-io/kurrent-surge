using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Turns a parsed expression into a delegate, resolving columns, coercing literals and selecting
/// comparisons once instead of on every instance. The tree it returns closes over typed accessors,
/// so evaluating it is a chain of direct calls with nothing left to look up.
/// </summary>
static class FilterCompiler {
    public static Func<T, bool?> Compile<T>(IFilterExpression expression, FilterSchema<T> schema) =>
        expression switch {
            AndExpression e        => And(Compile(e.Left, schema), Compile(e.Right, schema)),
            OrExpression e         => Or(Compile(e.Left, schema), Compile(e.Right, schema)),
            NotExpression e        => Not(Compile(e.Inner, schema)),
            ComparisonExpression e => Comparison(e, schema),
            IsNullExpression e     => IsNull(e, schema),
            LikeExpression e       => Like(e, schema),
            InExpression e         => In(e, schema),
            BetweenExpression e    => Between(e, schema),

            _ => throw new NotSupportedException($"Unhandled expression node '{expression.GetType().Name}'.")
        };

    #region ->> Logical <<-

    static Func<T, bool?> And<T>(Func<T, bool?> left, Func<T, bool?> right) =>
        // FALSE AND anything is FALSE, so the right side stays unevaluated — the one short circuit
        // three-valued logic still allows.
        t => left(t) is var result && result == false ? false : result & right(t);

    static Func<T, bool?> Or<T>(Func<T, bool?> left, Func<T, bool?> right) =>
        t => left(t) is var result && result == true ? true : result | right(t);

    static Func<T, bool?> Not<T>(Func<T, bool?> inner) =>
        t => !inner(t);

    #endregion

    #region ->> Predicates <<-

    static Func<T, bool?> Comparison<T>(ComparisonExpression expression, FilterSchema<T> schema) {
        var column = Resolve(expression.Property, schema, out var key);

        // The typed binding compares against the column's own value, so a transform rules it out.
        if (expression.Property.IsBare && column?.Accessor.BindComparison(expression.Op, expression.Value) is { } bound)
            return bound;

        var get   = Getter(column, key, expression.Property);
        var right = expression.Value.GetValue();
        var op    = expression.Op;

        return t => {
            var left = get(t);

            // Either side absent makes the comparison UNKNOWN — which is why `x = NULL` never matches
            // and IS NULL exists at all.
            if (left is null || right is null) return null;

            return Apply(op, ValueComparer.Compare(left, right));
        };
    }

    // The only predicate that is never UNKNOWN — answering that question is what it is for.
    static Func<T, bool?> IsNull<T>(IsNullExpression expression, FilterSchema<T> schema) {
        var get     = Getter(Resolve(expression.Property, schema, out var key), key, expression.Property);
        var negated = expression.Negated;

        return t => get(t) is null ? !negated : negated;
    }

    static Func<T, bool?> Like<T>(LikeExpression expression, FilterSchema<T> schema) {
        var get     = Getter(Resolve(expression.Property, schema, out var key), key, expression.Property);
        var matches = LikePattern.Bind(expression.Pattern, expression.CaseInsensitive);
        var negated = expression.Negated;

        return t => {
            var value = get(t);
            if (value is null) return null;

            var matched = matches(value as string ?? value.ToString() ?? "");
            return negated ? !matched : matched;
        };
    }

    static Func<T, bool?> In<T>(InExpression expression, FilterSchema<T> schema) {
        var get      = Getter(Resolve(expression.Property, schema, out var key), key, expression.Property);
        var negated  = expression.Negated;
        var values   = expression.Values;
        var hasNulls = false;

        for (var i = 0; i < values.Count; i++)
            hasNulls |= values[i] is NullFilterValue;

        var strings = BuildStringSet(values);

        return t => {
            var value = get(t);
            if (value is null) return null;

            if (strings is not null && value is string text)
                return negated ? !strings.Contains(text) : strings.Contains(text);

            for (var i = 0; i < values.Count; i++) {
                var candidate = values[i].GetValue();

                if (candidate is not null && ValueComparer.Compare(value, candidate) == 0)
                    return !negated;
            }

            // Without a match, a NULL in the list leaves the answer UNKNOWN: the value might have
            // equalled the thing the NULL stands for.
            return hasNulls ? null : negated;
        };

        static FrozenSet<string>? BuildStringSet(IReadOnlyList<FilterValue> values) {
            for (var i = 0; i < values.Count; i++) {
                if (values[i] is not StringFilterValue) return null;
            }

            var set = new HashSet<string>(values.Count, StringComparer.Ordinal);
            for (var i = 0; i < values.Count; i++)
                set.Add(((StringFilterValue)values[i]).Value);

            return set.ToFrozenSet(StringComparer.Ordinal);
        }
    }

    static Func<T, bool?> Between<T>(BetweenExpression expression, FilterSchema<T> schema) {
        var column = Resolve(expression.Property, schema, out var key);

        if (expression.Property.IsBare && column?.Accessor.BindRange(expression.Lower, expression.Upper, expression.Negated) is { } bound)
            return bound;

        var get     = Getter(column, key, expression.Property);
        var low     = expression.Lower.GetValue();
        var high    = expression.Upper.GetValue();
        var negated = expression.Negated;

        return t => {
            var value = get(t);

            // BETWEEN is `x >= a AND x <= b`, so a missing operand anywhere makes the range UNKNOWN.
            if (value is null || low is null || high is null) return null;

            var within = ValueComparer.Compare(value, low) >= 0 && ValueComparer.Compare(value, high) <= 0;
            return negated ? !within : within;
        };
    }

    #endregion

    #region ->> Column resolution <<-

    static FilterColumn<T>? Resolve<T>(PropertyAccess property, FilterSchema<T> schema, out string? key) =>
        schema.Find(property.PropertyName, property.Key, out key);

    // The schema rejects an undeclared column at parse time, so a null column cannot arrive here from
    // Parse. The absent-reads-as-null arm remains as the safe answer for any other entry point.
    static Func<T, object?> Getter<T>(FilterColumn<T>? column, string? key) =>
        column is null ? _ => null : column.Accessor.Getter(key);

    /// <summary>
    /// Reads a column and applies the calls written around it. Each transform compiles to a delegate
    /// once — the function body, its arguments and the cast target are all resolved here — so
    /// evaluating a record is a chain of direct calls with nothing left to look up.
    /// </summary>
    static Func<T, object?> Getter<T>(FilterColumn<T>? column, string? key, PropertyAccess property) {
        var get = Getter(column, key);

        if (property.IsBare) return get;

        var steps = new Func<object?, object?>[property.Transforms.Count];
        for (var i = 0; i < steps.Length; i++)
            steps[i] = Step(property.Transforms[i]);

        // A single transform is the common case, so it skips the loop entirely.
        if (steps.Length == 1) {
            var only = steps[0];
            return t => get(t) is { } value ? only(value) : null;
        }

        return t => {
            var value = get(t);

            // An array enumerates by struct, so this allocates nothing per record.
            foreach (var step in steps) {
                if (value is null) return null;
                value = step(value);
            }

            return value;
        };
    }

    static Func<object?, object?> Step(ColumnTransform transform) {
        switch (transform) {
            case CastTransform cast: {
                var target = FilterFunctions.ResolveTarget(cast.TargetType);
                var name   = cast.TargetType;
                var strict = cast.Strict;

                return value => {
                    // DuckDB fails the query on a bad CAST rather than dropping the row, so a strict
                    // cast raises here too. TRY_CAST is the NULL-returning form.
                    if (FilterFunctions.TryCast(value, target, out var converted)) return converted;

                    return strict ? throw FilterConversionException.Cast(value, name) : null;
                };
            }

            case FunctionTransform function: {
                var implementation = FilterFunctions.Resolve(function.Name);

                // The arguments are literals, so they are read once rather than per record.
                var arguments = new object?[function.Arguments.Count];
                for (var i = 0; i < arguments.Length; i++)
                    arguments[i] = function.Arguments[i].GetValue();

                return value => implementation(value, arguments);
            }

            default:
                throw new NotSupportedException($"Unhandled transform '{transform.GetType().Name}'.");
        }
    }

    static bool Apply(ComparisonOperator op, int comparison) =>
        op switch {
            ComparisonOperator.Equal              => comparison == 0,
            ComparisonOperator.NotEqual           => comparison != 0,
            ComparisonOperator.GreaterThan        => comparison > 0,
            ComparisonOperator.LessThan           => comparison < 0,
            ComparisonOperator.GreaterThanOrEqual => comparison >= 0,
            ComparisonOperator.LessThanOrEqual    => comparison <= 0,
            _                                     => false
        };

    #endregion
}

/// <summary>
/// Binds a SQL LIKE pattern to a matcher once. Everything but an interior wildcard is answerable with
/// a string operation, so only patterns carrying <c>_</c> or a mid-pattern <c>%</c> pay for a regex.
/// </summary>
static class LikePattern {
    public static Func<string, bool> Bind(string pattern, bool caseInsensitive) {
        var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (pattern.Length == 0) return text => text.Length == 0;
        if (pattern is "%")      return _ => true;

        if (!pattern.Contains('_')) {
            var first = pattern.IndexOf('%');

            if (first < 0) {
                var exact = pattern;
                return text => text.Equals(exact, comparison);
            }

            var last = pattern.LastIndexOf('%');

            if (first == pattern.Length - 1) {
                var prefix = pattern[..^1];
                return text => text.StartsWith(prefix, comparison);
            }

            if (first == 0 && last == 0) {
                var suffix = pattern[1..];
                return text => text.EndsWith(suffix, comparison);
            }

            if (first == 0 && last == pattern.Length - 1 && pattern.AsSpan(1, pattern.Length - 2).IndexOf('%') < 0) {
                var contains = pattern[1..^1];
                return text => text.Contains(contains, comparison);
            }
        }

        var regex = ToRegex(pattern, caseInsensitive);
        return regex.IsMatch;
    }

    /// <summary>
    /// Translates a LIKE pattern into an anchored regular expression. Shared with
    /// <see cref="FilterTerm.ToRegex"/>, which hands this text to a caller rather than matching with
    /// it — the two must agree on what the pattern means.
    /// </summary>
    public static string ToRegexPattern(string pattern) {
        var builder = new StringBuilder(pattern.Length + 4).Append('^');

        foreach (var ch in pattern) {
            if (ch == '%')
                builder.Append(".*");
            else if (ch == '_')
                builder.Append('.');
            else if (IsMetaChar(ch))
                builder.Append('\\').Append(ch);
            else
                builder.Append(ch);
        }

        return builder.Append('$').ToString();

        static bool IsMetaChar(char ch) =>
            ch is '\\' or '*' or '+' or '?' or '|' or '{' or '}' or '[' or ']' or '(' or ')' or '^' or '$' or '.';
    }

    static Regex ToRegex(string pattern, bool caseInsensitive) {
        var options = RegexOptions.Compiled | RegexOptions.Singleline;
        if (caseInsensitive) options |= RegexOptions.IgnoreCase;

        return new(ToRegexPattern(pattern), options);
    }
}
