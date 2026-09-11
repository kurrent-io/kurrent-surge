using Kurrent.Surge.RecordFilters;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// A filter that matches records using SQL-like filter expressions.
/// </summary>
[PublicAPI]
public record ExpressionFilter : RecordFilter {
    readonly Filter<Record> _filter;

    /// <inheritdoc/>
    public override string Expression { get; init; }

    /// <summary>
    /// The server-side filter extracted from the expression for optimization.
    /// Callers unwrap via <see cref="RecordFilterExtensions.UnwrapFilter(RecordFilter)"/>
    /// to determine wire routing. A stream constraint becomes a stream-identifier route, a schema
    /// constraint an index prefix or an event-type regex; an expression with neither still routes
    /// as the stream-identifier regex of <see cref="RecordFilter.ExcludeSystemRecords"/>.
    /// </summary>
    internal RecordFilter ServerFilter { get; }

    /// <summary>
    /// Creates a filter from a SQL-like expression.
    /// The expression is analyzed to extract server-side and client-side components.
    /// </summary>
    /// <param name="expression">A SQL-like filter expression (e.g., "stream = 'orders' AND properties.amount > 100")</param>
    /// <exception cref="FilterFormatException">The expression is not valid, or names a column no record carries.</exception>
    public ExpressionFilter(string expression) : this(expression, null) { }

    /// <summary>
    /// Creates a filter from a SQL-like expression, binding its <c>@name</c> placeholders.
    /// </summary>
    /// <param name="expression">A SQL-like filter expression.</param>
    /// <param name="parameters">Values for the expression's placeholders.</param>
    /// <exception cref="FilterFormatException">The expression is not valid, or a parameter is unbound.</exception>
    public ExpressionFilter(string expression, FilterParameters? parameters) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        // The template is kept as written. ToSql() renders the bound values, so a log can show both
        // the shape the caller wrote and what it resolved to.
        Expression = expression;

        _filter = RecordFilterSchema.Default.Parse(expression, parameters);

        var extraction = ServerFilterExtractor.Extract(_filter);
        ServerFilter             = extraction.ServerFilter;
        RequiresClientEvaluation = extraction.RequiresClientEvaluation;
    }

    /// <inheritdoc/>
    public override bool IsMatch(Record record) =>
        _filter.IsMatch(record);
}
