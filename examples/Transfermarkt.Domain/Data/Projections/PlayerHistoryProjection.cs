using System.Collections.Immutable;

namespace Transfermarkt.Domain;

/// <summary>
/// Complete transfer history for a player.
/// </summary>
[PublicAPI]
public sealed record PlayerHistoryProjection {
    PlayerHistoryProjection(
        PlayerId playerId,
        PlayerName playerName,
        ImmutableArray<CareerSpell> career
    ) {
        PlayerId   = playerId;
        PlayerName = playerName;
        Career     = career;
    }

    /// <summary>
    /// The player's identifier.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The player's name.
    /// </summary>
    public PlayerName PlayerName { get; }

    /// <summary>
    /// The player's career history as a sequence of club spells.
    /// </summary>
    public ImmutableArray<CareerSpell> Career { get; }

    /// <summary>
    /// Creates a PlayerHistoryProjection.
    /// </summary>
    public static PlayerHistoryProjection Create(
        PlayerId playerId,
        PlayerName playerName,
        ImmutableArray<CareerSpell>? career = null
    ) => new(playerId, playerName, career ?? []);
}
