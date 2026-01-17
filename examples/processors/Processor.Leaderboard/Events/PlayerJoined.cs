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
