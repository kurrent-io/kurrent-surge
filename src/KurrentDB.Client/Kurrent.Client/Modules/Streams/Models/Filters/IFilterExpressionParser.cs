namespace Kurrent.Client.Streams;

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

/// <summary>
/// Parses SQL-like filter expressions into executable filter objects.
/// </summary>
/// <remarks>
/// The parser supports DuckDB-compatible syntax including:
/// <list type="bullet">
///   <item><description>Comparison operators: =, !=, &lt;&gt;, &gt;, &lt;, &gt;=, &lt;=</description></item>
///   <item><description>Logical operators: AND, OR, NOT</description></item>
///   <item><description>Pattern matching: LIKE, ILIKE, NOT LIKE, NOT ILIKE</description></item>
///   <item><description>Null checking: IS NULL, IS NOT NULL, ISNULL, NOTNULL</description></item>
///   <item><description>Set membership: IN (...), NOT IN (...)</description></item>
///   <item><description>Range queries: BETWEEN x AND y, NOT BETWEEN x AND y</description></item>
///   <item><description>Property access: properties['key'], properties.key</description></item>
/// </list>
/// </remarks>
[PublicAPI]
public interface IFilterExpressionParser {
    /// <summary>
    /// Parses a filter expression string into an executable filter.
    /// </summary>
    /// <param name="input">The filter expression string to parse.</param>
    /// <returns>
    /// A result containing either the parsed <see cref="IFilterExpression"/> on success,
    /// or a <see cref="FilterParseError"/> on failure.
    /// </returns>
    /// <example>
    /// <code>
    /// var parser = new FilterExpressionParser();
    /// var result = parser.Parse("stream = 'orders' AND properties.amount > 100");
    ///
    /// if (result.IsSuccess) {
    ///     var filter = result.Value;
    ///     foreach (var record in records) {
    ///         if (filter.IsMatch(record)) {
    ///             // Process matching record
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    Result<IFilterExpression, FilterParseError> Parse(string input);

    /// <summary>
    /// Parses a filter expression from a character span into an executable filter.
    /// </summary>
    /// <param name="input">The filter expression span to parse.</param>
    /// <returns>
    /// A result containing either the parsed <see cref="IFilterExpression"/> on success,
    /// or a <see cref="FilterParseError"/> on failure.
    /// </returns>
    Result<IFilterExpression, FilterParseError> Parse(ReadOnlySpan<char> input);
}
