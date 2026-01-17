namespace Processor.ClickstreamSessions.Events;

/// <summary>
/// Event representing a page view in clickstream data.
/// </summary>
/// <param name="UserId">Identifier for the user (can be anonymous session ID).</param>
/// <param name="PageUrl">URL of the page viewed.</param>
/// <param name="Referrer">Referrer URL (empty if direct navigation).</param>
/// <param name="UserAgent">Browser user agent string.</param>
/// <param name="Timestamp">When the page was viewed.</param>
public record PageView(
    string UserId,
    string PageUrl,
    string Referrer,
    string UserAgent,
    DateTimeOffset Timestamp
);
