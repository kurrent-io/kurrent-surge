using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Base class for filters that match records by stream name.
/// </summary>
public abstract record StreamFilter : RecordFilter {
    protected Regex Regex { get; init; } = null!;

    public override bool IsMatch(Record record) =>
        Regex.IsMatch(record.Stream);

    protected static void RejectIndexStream(string value, string paramName) {
        if (value.Contains("$idx-", StringComparison.Ordinal)
         || value.Contains(@"\$idx-", StringComparison.Ordinal))
            throw new ArgumentException(GetIndexRedirectMessage(value), paramName);
        
        static string GetIndexRedirectMessage(string value) {
            var redirect = value switch {
                _ when value.Contains("$idx-ce-",   StringComparison.Ordinal) => $"{nameof(RecordFilter)}.ByCategory",
                _ when value.Contains("$idx-et-",   StringComparison.Ordinal) => $"{nameof(RecordFilter)}.BySchema",
                _ when value.Contains("$idx-user-", StringComparison.Ordinal) => $"{nameof(RecordFilter)}.ByUserIndex",
                _                                                             => $"{nameof(RecordFilter)}.ByCategory/BySchema/ByUserIndex"
            };

            return $"'{value}' targets an index stream. Use {redirect} instead.";
        }
    }
}

/// <summary>
/// A filter that matches records by an exact stream name.
/// Always has a <see cref="TargetStream"/> — routes to a direct stream read.
/// </summary>
public sealed partial record StreamNameFilter : StreamFilter {
    StreamNameFilter() { }

    /// <inheritdoc/>
    public override string Expression { get; init; } = null!;

    /// <summary>
    /// The target stream for direct stream read operations.
    /// </summary>
    public StreamName TargetStream { get; private init; }

    public static StreamNameFilter Parse(string value) =>
        TryParse(value, out var filter, out var error)
            ? filter
            : throw new ArgumentException(error, nameof(value));

    public static bool TryParse(string value, [NotNullWhen(true)] out StreamNameFilter? filter, out string? error) {
        filter = null;
        error  = null;

        value = value.Trim();

        if (value.Length == 0) {
            error = "Stream name filter value must not be empty.";
            return false;
        }

        if (StreamName.From(value).IsAllStream) {
            error = "Stream name filter cannot target '$all'.";
            return false;
        }

        try {
            RejectIndexStream(value, nameof(value));
        }
        catch (ArgumentException ex) {
            error = ex.Message;
            return false;
        }

        filter = new() {
            Expression   = value,
            TargetStream = StreamName.From(value),
            Regex        = new($"^{Regex.Escape(value)}$", RegexOptions.Compiled)
        };
        return true;
    }
}

/// <summary>
/// A filter that matches records where the stream name matches a regex pattern.
/// Routes through <c>$all</c> with a server-side regex filter.
/// </summary>
public sealed partial record StreamRegexFilter : StreamFilter {
    StreamRegexFilter() { }

    /// <inheritdoc/>
    public override string Expression { get; init; } = null!;

    public static StreamRegexFilter Parse(string pattern) =>
        TryParse(pattern, out var filter, out var error)
            ? filter
            : throw new ArgumentException(error, nameof(pattern));

    public static bool TryParse(string pattern, [NotNullWhen(true)] out StreamRegexFilter? filter, out string? error) {
        filter = null;
        error  = null;

        pattern = pattern.Trim();

        if (pattern.Length == 0) {
            error = "Stream regex filter pattern must not be empty.";
            return false;
        }

        try {
            RejectIndexStream(pattern, nameof(pattern));
        }
        catch (ArgumentException ex) {
            error = ex.Message;
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
