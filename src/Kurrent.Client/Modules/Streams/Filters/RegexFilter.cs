using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Base class for filters that match records using regex patterns.
/// </summary>
[PublicAPI]
public abstract record RegexFilter : IRecordFilter {
    /// <summary>
    /// Creates a regex filter for exact multi-value match.
    /// </summary>
    protected RegexFilter(string[] values) {
        if (values.Length == 0)
            throw new ArgumentException("At least one value is required.", nameof(values));

        var escaped = values.Select(v => Regex.Escape(v.Trim())).ToArray();

        var pattern = escaped.Length == 1
            ? $"^{escaped[0]}$"
            : $"^({string.Join("|", escaped)})$";

        Regex = new Regex(pattern, RegexOptions.Compiled);
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
