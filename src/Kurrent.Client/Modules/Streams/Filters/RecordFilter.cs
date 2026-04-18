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
    /// Creates a filter that matches a single stream name exactly.
    /// Rejects index stream names (<c>$idx-*</c>) — use <see cref="ByCategory"/>, <see cref="BySchema"/>,
    /// or <see cref="ByUserIndex"/> instead.
    /// </summary>
    /// <param name="stream">The stream name to match.</param>
    public static IRecordFilter ByStream(string stream) {
        ArgumentException.ThrowIfNullOrWhiteSpace(stream);
        ThrowIfIndexStreamName(stream, nameof(stream));
        return new StreamFilter(stream);
    }

    /// <summary>
    /// Creates a filter that targets the <c>$idx-ce-{category}</c> secondary index stream.
    /// </summary>
    /// <param name="category">The stream category name.</param>
    public static IRecordFilter ByCategory(string category) {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return new StreamFilter($"$idx-ce-{category}");
    }

    /// <summary>
    /// Creates a filter that targets the <c>$idx-et-{schema}</c> secondary index stream.
    /// </summary>
    /// <param name="schema">The schema (event type) name.</param>
    public static IRecordFilter BySchema(string schema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return new StreamFilter($"$idx-et-{schema}");
    }

    /// <summary>
    /// Creates a filter that targets the <c>$idx-user-{indexName}</c> user-defined secondary index stream.
    /// </summary>
    /// <param name="indexName">The user-defined index name.</param>
    public static IRecordFilter ByUserIndex(string indexName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        return new StreamFilter($"$idx-user-{indexName}");
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
    /// Rejects patterns that target index streams (<c>$idx-*</c>) — use <see cref="ByCategory"/>,
    /// <see cref="BySchema"/>, or <see cref="ByUserIndex"/> instead.
    /// </summary>
    /// <param name="pattern">The regex pattern to match stream names.</param>
    public static IRecordFilter ByStreamPattern(string pattern) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ThrowIfIndexStreamPattern(pattern, nameof(pattern));
        return new StreamFilter(new Regex(pattern, RegexOptions.Compiled));
    }

    static void ThrowIfIndexStreamName(string value, string paramName) {
        if (!value.StartsWith("$idx-", StringComparison.Ordinal))
            return;

        throw new ArgumentException(GetIndexRedirectMessage(value), paramName);
    }

    static void ThrowIfIndexStreamPattern(string pattern, string paramName) {
        // catch both literal "$idx-..." and the escaped regex form "\$idx-..."
        if (!pattern.Contains("$idx-", StringComparison.Ordinal) && !pattern.Contains(@"\$idx-", StringComparison.Ordinal))
            return;

        throw new ArgumentException(GetIndexRedirectMessage(pattern), paramName);
    }

    static string GetIndexRedirectMessage(string value) {
        var redirect = value switch {
            _ when value.Contains("$idx-ce-",   StringComparison.Ordinal) => $"{nameof(RecordFilter)}.{nameof(ByCategory)}",
            _ when value.Contains("$idx-et-",   StringComparison.Ordinal) => $"{nameof(RecordFilter)}.{nameof(BySchema)}",
            _ when value.Contains("$idx-user-", StringComparison.Ordinal) => $"{nameof(RecordFilter)}.{nameof(ByUserIndex)}",
            _ => $"{nameof(RecordFilter)}.{nameof(ByCategory)}/{nameof(BySchema)}/{nameof(ByUserIndex)}"
        };

        return $"'{value}' targets an index stream. Use {redirect} instead.";
    }
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
    StreamName TargetStream => StreamName.Log;

    /// <summary>
    /// The original filter expression.
    /// </summary>
    string Expression { get; }

    bool IsStreamFilter => TargetStream != StreamName.Log && TargetStream != StreamName.Unset;

    /// <summary>
    /// Whether this filter targets a secondary index stream (<c>$idx-*</c>).
    /// Short-hand for <c>IsStreamFilter &amp;&amp; TargetStream.IsIndexStream</c>.
    /// Request-shaping code should branch on this to route through the server's
    /// index dispatch path (<c>$all</c> + <c>Filter.Prefix</c>, never <c>Regex</c>).
    /// </summary>
    bool IsIndexFilter => IsStreamFilter && TargetStream.IsIndexStream;
}
