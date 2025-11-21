namespace Kurrent.Client.Streams;

/// <summary>
/// Factory methods for creating record filters.
/// </summary>
[PublicAPI]
public static class RecordFilter {
    /// <summary>
    /// A filter that matches all records.
    /// </summary>
    public static readonly IRecordFilter None = new EmptyFilter();

    /// <summary>
    /// Creates a filter that matches a specific stream exactly.
    /// </summary>
    /// <param name="stream">The stream name to match.</param>
    public static IRecordFilter ByStream(StreamName stream) => new StreamFilter(stream);

    /// <summary>
    /// Creates a filter that matches streams by prefix patterns.
    /// </summary>
    /// <param name="prefixes">The stream name prefixes to match.</param>
    public static IRecordFilter ByStreamPrefix(params string[] prefixes) => new StreamFilter(prefixes);

    /// <summary>
    /// Creates a filter that matches a specific schema (event type) exactly.
    /// </summary>
    /// <param name="schema">The schema name to match.</param>
    public static IRecordFilter BySchema(string schema) => new SchemaFilter(schema);

    /// <summary>
    /// Creates a filter that matches schemas by prefix patterns.
    /// </summary>
    /// <param name="prefixes">The schema name prefixes to match.</param>
    public static IRecordFilter BySchemaPrefix(params string[] prefixes) => new SchemaFilter(prefixes);

    /// <summary>
    /// Creates a filter from a DuckDB-compatible expression.
    /// </summary>
    /// <param name="expression">A filter expression (e.g., "stream = 'orders' AND props.ammount > 100").</param>
    public static IRecordFilter ByExpression(string expression) => new ExpressionFilter(expression);
}

/// <summary>
/// Defines a filter for matching records during read operations.
/// </summary>
[PublicAPI]
public interface IRecordFilter {
    /// <summary>
    /// Evaluates whether the specified record matches this filter.
    /// </summary>
    /// <param name="record">The record to evaluate.</param>
    /// <returns>True if the record matches the filter criteria; otherwise, false.</returns>
    bool IsMatch(Record record);

    /// <summary>
    /// The target stream for stream-specific read operations.
    /// </summary>
    StreamName TargetStream { get; }

    /// <summary>
    /// The original filter expression.
    /// </summary>
    string Expression { get; }
}
