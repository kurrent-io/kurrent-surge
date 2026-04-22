using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Base class for filters that match records using regex patterns.
/// </summary>
[PublicAPI]
public abstract record RegexFilter : IRecordFilter {
    /// <summary>
    /// Creates a regex filter for an exact single-value match.
    /// </summary>
    protected RegexFilter(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Regex = new($"^{Regex.Escape(value.Trim())}$", RegexOptions.Compiled);
    }

    /// <summary>
    /// Creates a regex filter from a compiled regex pattern.
    /// </summary>
    /// <param name="pattern">The compiled regex pattern.</param>
    protected RegexFilter(Regex pattern) {
        Regex = pattern;
    }

    Regex Regex { get; }

    /// <inheritdoc/>
    public string Expression => Regex.ToString();

    /// <inheritdoc/>
    public virtual StreamName TargetStream { get; protected set; } = StreamName.Log;

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
