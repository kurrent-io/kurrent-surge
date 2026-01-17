namespace Processor.Leaderboard.Events;

/// <summary>
/// Event representing a score submission in a game.
/// </summary>
/// <param name="GameId">Identifier for the game/leaderboard.</param>
/// <param name="PlayerId">Player who achieved the score.</param>
/// <param name="Score">The score achieved.</param>
/// <param name="SubmittedAt">When the score was submitted.</param>
public record ScoreSubmitted(
    string GameId,
    string PlayerId,
    long Score,
    DateTimeOffset SubmittedAt
);
