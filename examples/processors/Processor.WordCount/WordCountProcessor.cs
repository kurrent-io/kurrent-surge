using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Kurrent.Processors;
using Microsoft.Extensions.Logging;
using Processor.WordCount.Events;

namespace Processor.WordCount;

/// <summary>
/// Classic stream processing example: Word Count.
/// Maintains running word frequency counts across all processed text.
/// Demonstrates stateful aggregation and grouping patterns.
/// </summary>
public sealed partial class WordCountProcessor : IRecordProcessor {
    readonly ConcurrentDictionary<string, long> _wordCounts = new(StringComparer.OrdinalIgnoreCase);
    long _totalTextsProcessed;
    long _totalWordsProcessed;

    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is TextSubmitted text) {
            ProcessText(text, context.Logger);
        }

        return ValueTask.CompletedTask;
    }

    void ProcessText(TextSubmitted text, ILogger logger) {
        // Extract words using regex - handles punctuation and whitespace
        var words = WordPattern().Matches(text.Content)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length > 1); // Skip single-character "words"

        var wordCount = 0;
        foreach (var word in words) {
            _wordCounts.AddOrUpdate(word, 1, (_, count) => count + 1);
            wordCount++;
        }

        _totalTextsProcessed++;
        _totalWordsProcessed += wordCount;

        logger.LogDebug("Processed text {TextId}: {WordCount} words", text.TextId, wordCount);
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        // Log top 10 words at each checkpoint
        var topWords = _wordCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .ToList();

        context.Logger.LogInformation(
            "Checkpoint {CheckpointId} | Texts: {TextCount} | Words: {TotalWords} | Unique: {UniqueWords}",
            context.CheckpointId,
            _totalTextsProcessed,
            _totalWordsProcessed,
            _wordCounts.Count
        );

        if (topWords.Count > 0) {
            context.Logger.LogInformation("Top 10 words:");
            foreach (var (word, count) in topWords) {
                context.Logger.LogInformation("  {Word}: {Count}", word, count);
            }
        }

        return ValueTask.CompletedTask;
    }

    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordPattern();
}
