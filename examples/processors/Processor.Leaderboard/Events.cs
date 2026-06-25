namespace Processor.Leaderboard.Events;

/// <summary>
/// Event representing a player joining the game.
/// </summary>
/// <param name="PlayerId">Unique identifier for the player.</param>
/// <param name="DisplayName">Player's display name.</param>
/// <param name="Country">Player's country code (e.g., "US", "UK", "JP").</param>
/// <param name="JoinedAt">When the player joined.</param>
public record PlayerJoined(
    string PlayerId,
    string DisplayName,
    string Country,
    DateTimeOffset JoinedAt
);

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
