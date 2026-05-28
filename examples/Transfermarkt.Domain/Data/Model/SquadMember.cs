using Transfermarkt.Domain.Model;

namespace Transfermarkt.Data;

/// <summary>
/// A player in a squad projection.
/// </summary>
[PublicAPI]
public sealed record SquadMember {
    SquadMember(
        PlayerId playerId,
        PlayerName playerName,
        Position position,
        DateOnly joinedDate,
        bool isOnLoan,
        ClubId loanedFromClubId
    ) {
        PlayerId         = playerId;
        PlayerName       = playerName;
        Position         = position;
        JoinedDate       = joinedDate;
        IsOnLoan         = isOnLoan;
        LoanedFromClubId = loanedFromClubId;
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
    /// The player's position.
    /// </summary>
    public Position Position { get; }

    /// <summary>
    /// When the player joined this squad.
    /// </summary>
    public DateOnly JoinedDate { get; }

    /// <summary>
    /// Whether the player is at this club on loan.
    /// </summary>
    public bool IsOnLoan { get; }

    /// <summary>
    /// The parent club if on loan. <see cref="ClubId.None"/> if not on loan.
    /// </summary>
    public ClubId LoanedFromClubId { get; }

    /// <summary>
    /// Creates a SquadMember.
    /// </summary>
    public static SquadMember Create(
        PlayerId playerId,
        PlayerName playerName,
        Position position,
        DateOnly joinedDate,
        bool isOnLoan = false,
        ClubId? loanedFromClubId = null
    ) => new(
        playerId,
        playerName,
        position,
        joinedDate,
        isOnLoan,
        loanedFromClubId ?? ClubId.None
    );
}
