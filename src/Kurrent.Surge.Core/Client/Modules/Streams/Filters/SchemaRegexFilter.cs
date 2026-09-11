using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// A filter that matches records whose schema name matches a regex pattern.
/// Routes through <c>$all</c> with a server-side <c>event_type</c> regex filter.
/// </summary>
sealed record SchemaRegexFilter : RecordFilter {
    SchemaRegexFilter() { }

    /// <inheritdoc/>
    public override string Expression { get; init; } = null!;

    Regex Regex { get; init; } = null!;

    /// <inheritdoc/>
    public override bool IsMatch(Record record) =>
        Regex.IsMatch(record.Schema.Schema.Value);

    public static SchemaRegexFilter Parse(string pattern) =>
        TryParse(pattern, out var filter, out var error)
            ? filter
            : throw new ArgumentException(error, nameof(pattern));

    public static bool TryParse(string pattern, [NotNullWhen(true)] out SchemaRegexFilter? filter, out string? error) {
        filter = null;
        error  = null;

        pattern = pattern.Trim();

        if (pattern.Length == 0) {
            error = "Schema regex filter pattern must not be empty.";
            return false;
        }

        Regex regex;
        try {
            regex = new Regex(pattern, RegexOptions.Compiled);
        }
        catch (ArgumentException ex) {
            error = $"Invalid regex pattern '{pattern}': {ex.Message}";
            return false;
        }

        filter = new() {
            Expression = pattern,
            Regex      = regex
        };
        return true;
    }
}
