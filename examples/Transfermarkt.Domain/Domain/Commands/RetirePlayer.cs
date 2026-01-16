namespace Transfermarkt.Domain;

/// <summary>
/// Command to retire a player from professional football.
/// </summary>
[PublicAPI]
public sealed record RetirePlayer {
    RetirePlayer(PlayerId playerId, DateOnly retirementDate) {
        PlayerId       = playerId;
        RetirementDate = retirementDate;
    }

    /// <summary>
    /// The player being retired.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The date of retirement.
    /// </summary>
    public DateOnly RetirementDate { get; }

    /// <summary>
    /// Creates a RetirePlayer command.
    /// </summary>
    public static RetirePlayer Create(PlayerId playerId, DateOnly retirementDate) =>
        new(playerId, retirementDate);
}
