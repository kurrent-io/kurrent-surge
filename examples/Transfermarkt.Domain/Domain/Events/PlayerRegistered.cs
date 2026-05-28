using System.Collections.Immutable;
using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Events;

/// <summary>
/// A player is registered in the system for the first time.
/// </summary>
/// <remarks>
/// SAGA PATTERN: Emitted by a saga that watches for <see cref="PlayerTransferred"/>
/// events where <c>FromClubId</c> is <see cref="ClubId.None"/> (new player entering the system).
/// The saga enriches the event with full player details.
/// </remarks>
[PublicAPI]
public sealed record PlayerRegistered {
    /// <summary>
    /// The newly registered player's identifier.
    /// </summary>
    public PlayerId PlayerId { get; init; }

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
    /// The transfer that triggered this registration.
    /// </summary>
    public TransferId TriggeringTransferId { get; init; }

    /// <summary>
    /// When the player was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; }

}
