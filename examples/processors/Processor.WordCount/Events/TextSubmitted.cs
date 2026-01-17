namespace Processor.WordCount.Events;

/// <summary>
/// Event representing text content submitted for word counting.
/// </summary>
/// <param name="TextId">Unique identifier for this text submission.</param>
/// <param name="Content">The text content to be analyzed.</param>
/// <param name="SubmittedAt">When the text was submitted.</param>
public record TextSubmitted(
    string TextId,
    string Content,
    DateTimeOffset SubmittedAt
);
