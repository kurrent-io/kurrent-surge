using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Microsoft.Extensions.Logging;
using Processor.WordCount.Events;

namespace Processor.WordCount;

/// <summary>
/// Classic stream processing example: Word Count.
/// Maintains running word frequency counts across all processed text.
/// Demonstrates stateful aggregation and grouping patterns.
/// </summary>
public sealed partial class WordCountProcessor(ILogger<WordCountProcessor> logger) : IRecordProcessor {
    readonly ConcurrentDictionary<string, long> _wordCounts = new(StringComparer.OrdinalIgnoreCase);
    long _totalTextsProcessed;
    long _totalWordsProcessed;

    public ValueTask OnInitialize(InitializationContext context) {
        logger.LogInformation("Initializing WordCountProcessor: {ProcessorId}", context.Settings.ProcessorId);
        return ValueTask.CompletedTask;
    }

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            if (record.Value is TextSubmitted text) {
                ProcessText(text);
            }
        }

        return ValueTask.CompletedTask;
    }

    void ProcessText(TextSubmitted text) {
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

    public ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
        // Log top 10 words at each checkpoint
        var topWords = _wordCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .ToList();

        logger.LogInformation(
            "Checkpoint #{CheckpointNumber} | Texts: {TextCount} | Words: {TotalWords} | Unique: {UniqueWords}",
            context.Progress.Checkpoint.TotalCheckpoints,
            _totalTextsProcessed,
            _totalWordsProcessed,
            _wordCounts.Count
        );

        if (topWords.Count > 0) {
            logger.LogInformation("Top 10 words:");
            foreach (var (word, count) in topWords) {
                logger.LogInformation("  {Word}: {Count}", word, count);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnShutdown(ShutdownContext context) {
        logger.LogInformation(
            "Shutting down ({Reason}) | Final stats: {TextCount} texts, {TotalWords} words, {UniqueWords} unique",
            context.Reason,
            _totalTextsProcessed,
            _totalWordsProcessed,
            _wordCounts.Count
        );
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordPattern();
}
