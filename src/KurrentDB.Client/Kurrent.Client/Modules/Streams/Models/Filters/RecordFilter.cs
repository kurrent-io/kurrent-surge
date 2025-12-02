using System.Text.RegularExpressions;

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
    public static IRecordFilter ByStreamPrefix(params string[] prefixes) => new StreamFilter(prefixes, exactMatch: false);

    /// <summary>
    /// Creates a filter that matches any of the specified stream names exactly.
    /// </summary>
    /// <param name="streams">The stream names to match.</param>
    public static IRecordFilter ByStreams(params string[] streams) => new StreamFilter(streams, exactMatch: true);

    /// <summary>
    /// Creates a filter that matches a specific schema (event type) exactly.
    /// </summary>
    /// <param name="schema">The schema name to match.</param>
    public static IRecordFilter BySchema(string schema) => new SchemaFilter(schema);

    /// <summary>
    /// Creates a filter that matches schemas by prefix patterns.
    /// </summary>
    /// <param name="prefixes">The schema name prefixes to match.</param>
    public static IRecordFilter BySchemaPrefix(params string[] prefixes) => new SchemaFilter(prefixes, exactMatch: false);

    /// <summary>
    /// Creates a filter that matches any of the specified schema names exactly.
    /// </summary>
    /// <param name="schemas">The schema names to match.</param>
    public static IRecordFilter BySchemas(params string[] schemas) => new SchemaFilter(schemas, exactMatch: true);

    /// <summary>
    /// Creates a filter from a DuckDB-compatible expression.
    /// </summary>
    /// <param name="expression">A filter expression (e.g., "stream = 'orders' AND props.ammount > 100").</param>
    public static IRecordFilter ByExpression(string expression) => new ExpressionFilter(expression);

    /// <summary>
    /// Creates a stream filter from a raw regex pattern.
    /// Use standard regex syntax for modifiers (e.g., (?i) for case-insensitive).
    /// TargetStream is automatically derived for case-sensitive exact match patterns.
    /// </summary>
    /// <param name="pattern">The regex pattern to match stream names.</param>
    public static IRecordFilter ByStreamPattern(string pattern) =>
        new StreamFilter(new Regex(pattern, RegexOptions.Compiled));

    /// <summary>
    /// Creates a schema filter from a raw regex pattern.
    /// Use standard regex syntax for modifiers (e.g., (?i) for case-insensitive).
    /// </summary>
    /// <param name="pattern">The regex pattern to match schema names.</param>
    public static IRecordFilter BySchemaPattern(string pattern) =>
        new SchemaFilter(new Regex(pattern, RegexOptions.Compiled));
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
    bool IsMatch(Record record);

    /// <summary>
    /// The target stream for stream-specific read operations that defaults to the $all stream.
    /// </summary>
    StreamName TargetStream => StreamName.AllStream;

    /// <summary>
    /// The original filter expression.
    /// </summary>
    string Expression { get; }
}
