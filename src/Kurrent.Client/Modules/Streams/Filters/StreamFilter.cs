using System.Text.RegularExpressions;

namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches records by stream name.
/// </summary>
[PublicAPI]
public sealed record StreamFilter : RegexFilter {
    /// <summary>
    /// Creates a filter that matches any of the specified stream names exactly.
    /// When a single stream is provided, TargetStream is set for optimized reading.
    /// </summary>
    public StreamFilter(string[] streams) : base(streams) {
        if (streams.Length == 1)
            TargetStream = streams[0];
    }

    /// <summary>
    /// Creates a filter from a raw regex pattern.
    /// Automatically derives TargetStream for case-sensitive exact match patterns.
    /// </summary>
    /// <param name="pattern">The compiled regex pattern.</param>
    public StreamFilter(Regex pattern) : base(pattern) {
        // attempts to extract a literal stream name from an exact-match regex pattern.
        // returns false for patterns with wildcards, alternation, or case-insensitive modifiers.
        if (TryIdentifyTargetStream(pattern.ToString(), out var streamName))
            TargetStream = streamName;

        return;

        static bool TryIdentifyTargetStream(string pattern, out string streamName) {
            streamName = "";

            // Case-insensitive patterns can match multiple stream name variants
            if (pattern.StartsWith("(?i)", StringComparison.Ordinal))
                return false;

            // Must be anchored pattern: ^content$
            if (!pattern.StartsWith('^') || !pattern.EndsWith('$'))
                return false;

            // Extract content between anchors
            var content = pattern[1..^1];
            if (string.IsNullOrEmpty(content))
                return false;

            // Unescape the content to get the literal value
            string unescaped;
            try {
                unescaped = Regex.Unescape(content);
            }
            catch {
                return false;
            }

            // Re-escape and compare - if they match, it was a literal pattern
            var reescaped = Regex.Escape(unescaped);
            if (reescaped != content)
                // Pattern contains regex metacharacters (wildcards, alternation, etc.)
                return false;

            streamName = unescaped;

            return true;
        }
    }

    protected override ReadOnlySpan<char> GetMatchValue(Record record) => record.Stream;
}
