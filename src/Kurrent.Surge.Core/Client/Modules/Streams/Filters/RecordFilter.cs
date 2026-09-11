using System.Diagnostics.CodeAnalysis;
using Kurrent.Surge.RecordFilters;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Base type for all record filters. Use the static factory methods to create filters.
/// </summary>
[PublicAPI]
public abstract record RecordFilter : IParsable<RecordFilter> {
    /// <summary>
    /// The raw log: every record, system records and unresolved link rows included. The entry
    /// point for replication.
    /// </summary>
    public static readonly RecordFilter None = new EmptyFilter();

    /// <summary>
    /// Excludes every record whose stream name or schema name starts with <c>$</c>: the records the
    /// server and the projections write for themselves. The default filter for application reads
    /// of the log.
    /// </summary>
    public static readonly RecordFilter ExcludeSystemRecords = new SystemRecordFilter();

    /// <summary>
    /// Parses a filter expression. Empty input yields <see cref="None"/>.
    /// </summary>
    /// <remarks>
    /// Present so a filter can be written as its expression wherever a scalar is expected —
    /// configuration binding above all. Every filter shape reduces to an expression, so the
    /// expression is the lossless text form.
    /// </remarks>
    /// <param name="s">The filter expression.</param>
    /// <param name="provider">Unused. Filter expressions are culture-invariant.</param>
    public static RecordFilter Parse(string s, IFormatProvider? provider) =>
        string.IsNullOrWhiteSpace(s) ? None : ByExpression(s);

    /// <inheritdoc cref="Parse"/>
    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        IFormatProvider? provider,
        [MaybeNullWhen(false)] out RecordFilter result
    ) {
        try {
            result = Parse(s ?? "", provider);
            return true;
        }
        catch (Exception) {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// The original filter expression.
    /// </summary>
    public abstract string Expression { get; init; }

    /// <summary>
    /// Evaluates whether the specified record matches this filter.
    /// </summary>
    /// <param name="record">The record to evaluate.</param>
    public abstract bool IsMatch(Record record);

    /// <summary>
    /// Whether the SDK must run <see cref="IsMatch"/> on the client beyond what server-side routing
    /// already does. Server-routable filters leave this <c>false</c> — the server delivers only
    /// matches, so re-evaluating client-side would filter exactly what the server already filtered.
    /// Only a composite filter that the server cannot fully apply (an <see cref="ExpressionFilter"/>
    /// with a residual predicate, or one with no server form at all) sets it <c>true</c>; such a
    /// subscription discards records the consumer never sees, so it must raise synthetic scan
    /// checkpoints to carry progress across those gaps.
    /// </summary>
    public bool RequiresClientEvaluation { get; init; }

    /// <summary>
    /// Creates a filter that targets the <c>$idx-ce-{category}</c> secondary index stream.
    /// </summary>
    /// <param name="categoryName">The stream category name.</param>
    public static RecordFilter ByCategory(string categoryName) =>
        CategoryFilter.Parse(categoryName);

    /// <summary>
    /// Creates a filter that targets the <c>$idx-et-{schema}</c> secondary index stream.
    /// </summary>
    /// <param name="schemaName">The schema (event type) name.</param>
    public static RecordFilter BySchema(string schemaName) =>
        SchemaFilter.Parse(schemaName);

    /// <summary>
    /// Creates a filter that targets the <c>$idx-user-{indexName}</c> user-defined secondary index stream.
    /// </summary>
    /// <param name="indexName">The user-defined index name.</param>
    public static RecordFilter ByUserIndex(string indexName) =>
        UserIndexFilter.Parse(indexName);

    /// <summary>
    /// Creates a filter from a DuckDB-compatible SQL expression.
    /// </summary>
    /// <remarks>
    /// On the log an expression filter never delivers a system record: it is combined with
    /// <see cref="ExcludeSystemRecords"/>. Subscribe with <see cref="None"/> to read the raw log.
    /// </remarks>
    /// <param name="expression">A filter expression (e.g., "stream = 'orders' AND props.amount > 100").</param>
    public static RecordFilter ByExpression(string expression) =>
        new ExpressionFilter(expression);

    /// <summary>
    /// Creates a filter from a DuckDB-compatible SQL expression, binding its <c>@name</c> placeholders.
    /// A bound value becomes a literal in the parsed expression rather than text spliced into it.
    /// </summary>
    /// <remarks>
    /// On the log an expression filter never delivers a system record: it is combined with
    /// <see cref="ExcludeSystemRecords"/>. Subscribe with <see cref="None"/> to read the raw log.
    /// </remarks>
    /// <param name="expression">A filter expression (e.g., "stream = @stream AND revision > @from").</param>
    /// <param name="parameters">Values for the expression's placeholders.</param>
    public static RecordFilter ByExpression(string expression, FilterParameters parameters) =>
        new ExpressionFilter(expression, parameters);

    /// <summary>
    /// Creates a filter that matches a single stream name exactly.
    /// Rejects index stream names (<c>$idx-*</c>) — use <see cref="ByCategory"/>, <see cref="BySchema"/>,
    /// or <see cref="ByUserIndex"/> instead.
    /// </summary>
    /// <param name="streamName">The stream name to match.</param>
    public static RecordFilter ByStream(StreamName streamName) =>
        StreamNameFilter.Parse(streamName);

    /// <summary>
    /// Creates a stream filter from a raw regex pattern.
    /// Rejects patterns that target index streams (<c>$idx-*</c>) — use <see cref="ByCategory"/>,
    /// <see cref="BySchema"/>, or <see cref="ByUserIndex"/> instead.
    /// </summary>
    /// <param name="pattern">The regex pattern to match stream names.</param>
    public static RecordFilter ByStreamPattern(string pattern) =>
        StreamRegexFilter.Parse(pattern);
}
