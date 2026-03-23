using static System.String;

namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches records using SQL-like filter expressions.
/// </summary>
[PublicAPI]
public record ExpressionFilter : IRecordFilter {
    readonly IFilterExpression _parsedExpression;

    /// <inheritdoc/>
    public StreamName TargetStream { get; }

    /// <inheritdoc/>
    public string Expression { get; }

    /// <summary>
    /// The server-side filter extracted from the expression for optimization.
    /// This is used by the V1 mapper to send filtering to the server.
    /// </summary>
    internal IRecordFilter ServerFilter { get; }

    /// <summary>
    /// Creates a filter from a SQL-like expression.
    /// The expression is analyzed to extract server-side and client-side components.
    /// </summary>
    /// <param name="expression">A SQL-like filter expression (e.g., "stream = 'orders' AND properties.amount > 100")</param>
    public ExpressionFilter(string expression) {
        if (IsNullOrWhiteSpace(expression))
            throw new ArgumentNullException(nameof(expression));

        Expression = expression;

        _parsedExpression = ExpressionFilterParser.Parse(expression);

        ServerFilter = ServerFilterExtractor.Extract(_parsedExpression);
        TargetStream = ServerFilter.TargetStream != StreamName.Unset
            ? ServerFilter.TargetStream
            : StreamName.All;
    }

    /// <inheritdoc/>
    public bool IsMatch(Record record) =>
        _parsedExpression.IsMatch(record);
}
