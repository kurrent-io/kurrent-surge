namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches records using SQL-like filter expressions.
/// </summary>
[PublicAPI]
public record ExpressionFilter : RecordFilter {
    readonly IFilterExpression _parsedExpression;

    /// <inheritdoc/>
    public override string Expression { get; init; }

    /// <summary>
    /// Returns <c>true</c> if the expression contains predicates that access the record's
    /// JSON payload (<c>data</c> column). Used to validate that decoding is enabled.
    /// </summary>
    public bool QueriesData { get; }

    /// <summary>
    /// The server-side filter extracted from the expression for optimization.
    /// Callers unwrap via <see cref="RecordFilterExtensions.UnwrapFilter(RecordFilter)"/>
    /// to determine wire routing.
    /// </summary>
    internal RecordFilter ServerFilter { get; }

    /// <summary>
    /// Creates a filter from a SQL-like expression.
    /// The expression is analyzed to extract server-side and client-side components.
    /// </summary>
    /// <param name="expression">A SQL-like filter expression (e.g., "stream = 'orders' AND properties.amount > 100")</param>
    public ExpressionFilter(string expression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        Expression = expression;

        _parsedExpression = ExpressionFilterParser.Parse(expression);

        QueriesData  = _parsedExpression.QueriesData;
        ServerFilter = ServerFilterExtractor.Extract(_parsedExpression);
    }

    /// <inheritdoc/>
    public override bool IsMatch(Record record) =>
        _parsedExpression.IsMatch(record);
}
