namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// How a column carries its value, on both sides of the mapping. The kind decides how a key is
/// applied when reading in .NET, and which DuckDB access form the column renders to.
/// </summary>
enum FilterColumnKind {
    /// <summary>
    /// A plain value. Takes no key. Renders as the bare column name.
    /// </summary>
    Scalar,

    /// <summary>
    /// A string-keyed bag. Renders as <c>column-&gt;&gt;'key'</c>.
    /// </summary>
    Map,

    /// <summary>
    /// A nested JSON document. Renders as <c>column-&gt;&gt;'$.path'</c>, so a dotted path
    /// reaches nested members through a single JSONPath expression.
    /// </summary>
    Json
}

/// <summary>
/// One declared column: the .NET accessor, the canonical DuckDB column name, and the access form
/// shared by both. Aliases resolve to the same instance, which is what lets an alias render as its
/// canonical name.
/// </summary>
sealed class FilterColumn<T>(string name, FilterColumnKind kind, ColumnAccessor<T> accessor) {
    public string            Name     { get; } = name;
    public FilterColumnKind  Kind     { get; } = kind;
    public ColumnAccessor<T> Accessor { get; } = accessor;

    public bool AcceptsKey => Kind is not FilterColumnKind.Scalar;
}

/// <summary>
/// Reads a column from an instance, and — when it knows the value's type — binds a predicate over it
/// ahead of time.
/// <para>
/// <see cref="Getter"/> is the fallback every column supports: it costs a box for a value type and
/// leaves the comparison to be resolved by inspecting the boxed value. The <c>Bind</c> members exist
/// to avoid that. They return <c>null</c> when this accessor cannot specialise for the given literal,
/// which sends the caller back to the fallback rather than changing what the expression means.
/// </para>
/// </summary>
abstract class ColumnAccessor<T> {
    /// <summary>
    /// Reads the column, boxing value types. <paramref name="key"/> is applied by keyed columns and
    /// ignored by scalars.
    /// </summary>
    public abstract Func<T, object?> Getter(string? key);

    public virtual Func<T, bool?>? BindComparison(ComparisonOperator op, FilterValue literal) => null;

    public virtual Func<T, bool?>? BindRange(FilterValue lower, FilterValue upper, bool negated) => null;
}

/// <summary>
/// Holds the accessor at its declared return type, so a predicate can be bound directly against
/// <typeparamref name="TValue"/> with no boxing and no per-record type inspection.
/// </summary>
sealed class ScalarAccessor<T, TValue>(Func<T, TValue> getter) : ColumnAccessor<T> {
    // Comparer<string>.Default is culture-sensitive. Column values compare ordinally, matching the
    // database, so string columns must not take the default comparer.
    static readonly IComparer<TValue> Comparer =
        typeof(TValue) == typeof(string)
            ? (IComparer<TValue>)(object)StringComparer.Ordinal
            : Comparer<TValue>.Default;

    public override Func<T, object?> Getter(string? key) {
        var get = getter;
        return t => get(t);
    }

    public override Func<T, bool?>? BindComparison(ComparisonOperator op, FilterValue literal) {
        if (!TryCoerce(literal, out var target)) return null;

        var get      = getter;
        var comparer = Comparer;

        return op switch {
            ComparisonOperator.Equal              => t => Compare(t) is { } c ? c == 0 : null,
            ComparisonOperator.NotEqual           => t => Compare(t) is { } c ? c != 0 : null,
            ComparisonOperator.GreaterThan        => t => Compare(t) is { } c ? c > 0  : null,
            ComparisonOperator.LessThan           => t => Compare(t) is { } c ? c < 0  : null,
            ComparisonOperator.GreaterThanOrEqual => t => Compare(t) is { } c ? c >= 0 : null,
            ComparisonOperator.LessThanOrEqual    => t => Compare(t) is { } c ? c <= 0 : null,
            _                                     => null
        };

        // `v is null` folds away for a non-nullable TValue, so value-typed columns pay nothing for it.
        int? Compare(T instance) {
            var v = get(instance);
            return v is null ? null : comparer.Compare(v, target);
        }
    }

    public override Func<T, bool?>? BindRange(FilterValue lower, FilterValue upper, bool negated) {
        if (!TryCoerce(lower, out var low) || !TryCoerce(upper, out var high)) return null;

        var get      = getter;
        var comparer = Comparer;

        return t => {
            var v = get(t);
            if (v is null) return null;

            var within = comparer.Compare(v, low) >= 0 && comparer.Compare(v, high) <= 0;
            return negated ? !within : within;
        };
    }

    /// <summary>
    /// Converts a literal to <typeparamref name="TValue"/>. Returns <c>false</c> when the conversion
    /// would change what the comparison means — a non-integral literal against an integer column, a
    /// value out of range, or a literal of an unrelated kind — leaving the caller on the fallback path
    /// where cross-type comparison is handled.
    /// </summary>
    static bool TryCoerce(FilterValue literal, out TValue result) {
        result = default!;

        switch (literal) {
            case StringFilterValue s when typeof(TValue) == typeof(string):
                result = (TValue)(object)s.Value;
                return true;

            case BooleanFilterValue b when typeof(TValue) == typeof(bool):
                result = (TValue)(object)b.Value;
                return true;

            case NumberFilterValue n:
                return TryCoerceNumber(n.Value, out result);

            default:
                return false;
        }
    }

    static bool TryCoerceNumber(decimal value, out TValue result) {
        result = default!;

        if (typeof(TValue) == typeof(decimal)) {
            result = (TValue)(object)value;
            return true;
        }

        if (typeof(TValue) == typeof(double)) {
            result = (TValue)(object)(double)value;
            return true;
        }

        // An integer column only takes an integral literal in range. `revision = 150.5` must not
        // truncate into a match against 150 — it falls back and compares as decimal instead.
        if (value != decimal.Truncate(value)) return false;

        if (typeof(TValue) == typeof(long)) {
            if (value < long.MinValue || value > long.MaxValue) return false;
            result = (TValue)(object)(long)value;
            return true;
        }

        if (typeof(TValue) == typeof(int)) {
            if (value < int.MinValue || value > int.MaxValue) return false;
            result = (TValue)(object)(int)value;
            return true;
        }

        return false;
    }
}

/// <summary>
/// A column whose values have no declared type — a metadata bag or a JSON document. It cannot bind
/// ahead of time, so it always takes the fallback path.
/// </summary>
sealed class KeyedAccessor<T>(Func<T, string?, object?> getter) : ColumnAccessor<T> {
    public override Func<T, object?> Getter(string? key) {
        var get = getter;
        return t => get(t, key);
    }
}
