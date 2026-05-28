using System.Collections.Immutable;
using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Commands;

/// <summary>
/// Command to register a new player in the system.
/// </summary>
[PublicAPI]
public sealed record RegisterPlayer {
    /// <summary>
    /// The player's name.
    /// </summary>
    public PlayerName Name { get; init; }

    /// <summary>
    /// The player's date of birth.
    /// </summary>
    public DateOnly DateOfBirth { get; init; }

    /// <summary>
    /// The player's nationality.
    /// </summary>
    public Country Nationality { get; init; }

    /// <summary>
    /// The player's main position.
    /// </summary>
    public Position MainPosition { get; init; }

    /// <summary>
    /// Additional positions the player can play.
    /// </summary>
    public ImmutableArray<Position> OtherPositions { get; init; }

    /// <summary>
    /// The club where the player is initially registered.
    /// </summary>
    public ClubId InitialClubId { get; init; }

    /// <summary>
    /// The date the registration becomes effective.
    /// </summary>
    public DateOnly EffectiveDate { get; init; }
}
