using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Base class for filters that match records using regex patterns.
/// </summary>
[PublicAPI]
public abstract record RegexFilter : IRecordFilter {
    /// <summary>
    /// Creates a regex filter for exact match.
    /// </summary>
    protected RegexFilter(string value) {
        var escaped = Regex.Escape(value);
        Regex = new Regex($"^{escaped}$", RegexOptions.Compiled);
    }

    /// <summary>
    /// Creates a regex filter for prefix match.
    /// </summary>
    protected RegexFilter(string[] prefixes) {
        if (prefixes.Length == 0)
            throw new ArgumentException("At least one prefix is required.", nameof(prefixes));

        var escaped = prefixes.Select(p => Regex.Escape(p.Trim()));
        Regex = new Regex($"^({string.Join("|", escaped)})", RegexOptions.Compiled);
    }

    Regex Regex { get; }

    /// <inheritdoc/>
    public string Expression => Regex.ToString();

    /// <inheritdoc/>
    public virtual StreamName TargetStream { get; protected set; } = StreamName.All;

    /// <summary>
    /// Evaluates whether the specified record matches this filter.
    /// </summary>
    public virtual bool IsMatch(Record record) =>
        Regex.IsMatch(GetMatchValue(record));

    /// <summary>
    /// Gets the value from the record to match against the pattern.
    /// </summary>
    protected abstract ReadOnlySpan<char> GetMatchValue(Record record);
}
