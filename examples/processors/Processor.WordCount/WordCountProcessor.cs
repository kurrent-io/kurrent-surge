using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Kurrent.Streaming;
using Microsoft.Extensions.Logging;
using Processor.WordCount.Events;

namespace Processor.WordCount;

/// <summary>
/// Classic stream processing example: Word Count.
/// Maintains running word frequency counts across all processed text.
/// Demonstrates stateful aggregation and grouping patterns.
/// </summary>
public sealed partial class WordCountProcessor : MessageProcessor {
    readonly ConcurrentDictionary<string, long> _wordCounts = new(StringComparer.OrdinalIgnoreCase);
    
    long _totalTextsProcessed;
    long _totalWordsProcessed;
    
    public WordCountProcessor() {
        Process<TextSubmitted>((msg, ctx) => {
            // Extract words using regex - handles punctuation and whitespace
            var words = WordPattern().Matches(msg.Content)
                .Select(m => m.Value.ToLowerInvariant())
                .Where(w => w.Length > 1); // Skip single-character "words"

            var wordCount = 0;
            foreach (var word in words) {
                _wordCounts.AddOrUpdate(word, 1, (_, count) => count + 1);
                wordCount++;
            }

            _totalTextsProcessed++;
            _totalWordsProcessed += wordCount;

            ctx.Logger.LogDebug("Processed text {TextId}: {WordCount} words", msg.TextId, wordCount);
        });
        
        OnCheckpoint(ctx => {
            ctx.Logger.LogInformation(
                "Checkpoint {CheckpointId} | Texts: {TextCount} | Words: {TotalWords} | Unique: {UniqueWords}",
                ctx.CheckpointId, _totalTextsProcessed, _totalWordsProcessed, _wordCounts.Count
            );
            
            // Log top 10 words at each checkpoint
            var topWords = _wordCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToList();

            if (topWords.Count > 0) {
                ctx.Logger.LogInformation("Top 10 words:");
                foreach (var (word, count) in topWords)
                    ctx.Logger.LogInformation("  {Word}: {Count}", word, count);
            }

            return ValueTask.CompletedTask;
        });
    }

    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordPattern();
}
