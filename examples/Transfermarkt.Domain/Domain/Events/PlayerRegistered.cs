using System.Collections.Immutable;

namespace Transfermarkt.Domain;

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
    PlayerRegistered(
        PlayerId playerId,
        PlayerName name,
        DateOnly dateOfBirth,
        Country nationality,
        Position mainPosition,
        ImmutableArray<Position> otherPositions,
        ClubId initialClubId,
        TransferId triggeringTransferId,
        DateTimeOffset registeredAt
    ) {
        PlayerId              = playerId;
        Name                  = name;
        DateOfBirth           = dateOfBirth;
        Nationality           = nationality;
        MainPosition          = mainPosition;
        OtherPositions        = otherPositions;
        InitialClubId         = initialClubId;
        TriggeringTransferId  = triggeringTransferId;
        RegisteredAt          = registeredAt;
    }

    /// <summary>
    /// The newly registered player's identifier.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The player's name.
    /// </summary>
    public PlayerName Name { get; }

    /// <summary>
    /// The player's date of birth.
    /// </summary>
    public DateOnly DateOfBirth { get; }

    /// <summary>
    /// The player's nationality.
    /// </summary>
    public Country Nationality { get; }

    /// <summary>
    /// The player's main position.
    /// </summary>
    public Position MainPosition { get; }

    /// <summary>
    /// Additional positions the player can play.
    /// </summary>
    public ImmutableArray<Position> OtherPositions { get; }

    /// <summary>
    /// The club where the player is initially registered.
    /// </summary>
    public ClubId InitialClubId { get; }

    /// <summary>
    /// The transfer that triggered this registration.
    /// </summary>
    public TransferId TriggeringTransferId { get; }

    /// <summary>
    /// When the player was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; }

    /// <summary>
    /// Creates a PlayerRegistered event.
    /// </summary>
    public static PlayerRegistered Create(
        PlayerId playerId,
        PlayerName name,
        DateOnly dateOfBirth,
        Country nationality,
        Position mainPosition,
        ImmutableArray<Position> otherPositions,
        ClubId initialClubId,
        TransferId triggeringTransferId,
        DateTimeOffset? registeredAt = null
    ) => new(
        playerId,
        name,
        dateOfBirth,
        nationality,
        mainPosition,
        otherPositions,
        initialClubId,
        triggeringTransferId,
        registeredAt ?? DateTimeOffset.UtcNow
    );
}
