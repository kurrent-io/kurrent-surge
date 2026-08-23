namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// A filter expression parsed against a <see cref="FilterSchema{T}"/>. Evaluate it in .NET with
/// <see cref="IsMatch"/>, or render it as SQL with <see cref="ToSql"/>; both read the same column
/// declarations, so the two sides cannot drift.
/// </summary>
/// <remarks>
/// Immutable and safe to share across threads. The predicate is compiled once at construction, so
/// column resolution, literal coercion and comparison selection are all settled before the first
/// instance is evaluated.
/// </remarks>
/// <typeparam name="T">The type this filter is evaluated against.</typeparam>
public sealed class Filter<T> {
    readonly IFilterExpression _expression;
    readonly FilterSchema<T>   _schema;
    readonly Func<T, bool?>    _predicate;

    internal Filter(string expression, IFilterExpression parsed, FilterSchema<T> schema) {
        Expression  = expression;
        _expression = parsed;
        _schema     = schema;
        _predicate  = FilterCompiler.Compile(parsed, schema);
    }

    /// <summary>
    /// The expression as written.
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// Evaluates the expression against an instance. Only a predicate that resolves to true selects —
    /// one that resolves to UNKNOWN does not, which is how a SQL WHERE clause treats it.
    /// </summary>
    public bool IsMatch(T instance) => _predicate(instance) == true;

    /// <summary>
    /// Renders the expression as a SQL predicate, with every alias replaced by its canonical column
    /// name and every keyed access written in arrow notation.
    /// </summary>
    public string ToSql() => DuckDbRenderer.Render(_expression, _schema);

    /// <summary>
    /// What this expression constrains a column to, for a caller that cannot hand the whole expression
    /// to its store. Returns <see cref="FilterConstraint.None"/> when it says nothing usable.
    /// </summary>
    /// <param name="column">A declared column name or alias.</param>
    public FilterConstraint ConstraintOn(string column) =>
        ConstraintAnalyzer.Analyze(_expression, _schema, column);

    public override string ToString() => Expression;
}
