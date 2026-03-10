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
    /// Creates a filter that matches one or more stream names exactly.
    /// </summary>
    /// <param name="streams">The stream names to match.</param>
    public static IRecordFilter ByStream(params string[] streams) {
        ArgumentOutOfRangeException.ThrowIfZero(streams.Length, nameof(streams));
        return new StreamFilter(streams);
    }

    /// <summary>
    /// Creates a filter that targets the <c>$idx-ce-{category}</c> secondary index stream.
    /// </summary>
    /// <param name="category">The stream category name.</param>
    public static IRecordFilter ByCategory(string category) {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return new StreamFilter([$"$idx-ce-{category}"]);
    }

    /// <summary>
    /// Creates a filter that targets the <c>$idx-et-{schema}</c> secondary index stream.
    /// </summary>
    /// <param name="schema">The schema (event type) name.</param>
    public static IRecordFilter BySchema(string schema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return new StreamFilter([$"$idx-et-{schema}"]);
    }

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
