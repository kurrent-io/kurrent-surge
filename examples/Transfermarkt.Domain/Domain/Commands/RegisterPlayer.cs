using System.Collections.Immutable;

namespace Transfermarkt.Domain;

/// <summary>
/// Command to register a new player in the system.
/// </summary>
[PublicAPI]
public sealed record RegisterPlayer {
    RegisterPlayer(
        PlayerName name,
        DateOnly dateOfBirth,
        Country nationality,
        Position mainPosition,
        ImmutableArray<Position> otherPositions,
        ClubId initialClubId,
        DateOnly effectiveDate
    ) {
        Name           = name;
        DateOfBirth    = dateOfBirth;
        Nationality    = nationality;
        MainPosition   = mainPosition;
        OtherPositions = otherPositions;
        InitialClubId  = initialClubId;
        EffectiveDate  = effectiveDate;
    }

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
    /// The date the registration becomes effective.
    /// </summary>
    public DateOnly EffectiveDate { get; }

    /// <summary>
    /// Creates a RegisterPlayer command.
    /// </summary>
    public static RegisterPlayer Create(
        PlayerName name,
        Country nationality,
        Position mainPosition,
        DateOnly dateOfBirth = default,
        ClubId initialClubId = default,
        DateOnly effectiveDate = default,
        ImmutableArray<Position>? otherPositions = null
    ) => new(
        name,
        dateOfBirth,
        nationality,
        mainPosition,
        otherPositions ?? [],
        initialClubId == default ? ClubId.Unknown : initialClubId,
        effectiveDate
    );
}
