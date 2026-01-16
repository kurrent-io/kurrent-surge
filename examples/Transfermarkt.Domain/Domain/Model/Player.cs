using System.Collections.Immutable;

namespace Transfermarkt.Domain;

/// <summary>
/// Player reference data. Updated via events.
/// </summary>
[PublicAPI]
public readonly record struct Player {
    Player(
        PlayerId id,
        PlayerName name,
        DateOnly dateOfBirth,
        Country nationality,
        Position mainPosition,
        ImmutableArray<Position> otherPositions
    ) {
        Id             = id;
        Name           = name;
        DateOfBirth    = dateOfBirth;
        Nationality    = nationality;
        MainPosition   = mainPosition;
        OtherPositions = otherPositions;
    }

    /// <summary>
    /// Unique player identifier.
    /// </summary>
    public PlayerId Id { get; }

    /// <summary>
    /// Player's name.
    /// </summary>
    public PlayerName Name { get; }

    /// <summary>
    /// Player's date of birth.
    /// </summary>
    public DateOnly DateOfBirth { get; }

    /// <summary>
    /// Player's primary nationality.
    /// </summary>
    public Country Nationality { get; }

    /// <summary>
    /// Player's primary playing position.
    /// </summary>
    public Position MainPosition { get; }

    /// <summary>
    /// Additional positions the player can play.
    /// </summary>
    public ImmutableArray<Position> OtherPositions { get; }

    /// <summary>
    /// Creates a Player from the specified values.
    /// </summary>
    public static Player From(
        PlayerId id,
        PlayerName name,
        DateOnly dateOfBirth,
        Country nationality,
        Position mainPosition,
        ImmutableArray<Position>? otherPositions = null
    ) => new(id, name, dateOfBirth, nationality, mainPosition, otherPositions ?? []);

    public override string ToString() => Name.DisplayName;
}
