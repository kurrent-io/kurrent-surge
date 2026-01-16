namespace Transfermarkt.Domain;

/// <summary>
/// A player retires from professional football.
/// </summary>
/// <remarks>
/// SAGA PATTERN: Emitted by a saga that watches for <see cref="PlayerTransferred"/>
/// events where <c>ToClubId</c> is <see cref="ClubId.None"/> (player leaving the system).
/// </remarks>
[PublicAPI]
public sealed record PlayerRetired {
    PlayerRetired(
        PlayerId playerId,
        PlayerName playerName,
        ClubId lastClubId,
        DateOnly retirementDate,
        TransferId triggeringTransferId,
        DateTimeOffset retiredAt
    ) {
        PlayerId             = playerId;
        PlayerName           = playerName;
        LastClubId           = lastClubId;
        RetirementDate       = retirementDate;
        TriggeringTransferId = triggeringTransferId;
        RetiredAt            = retiredAt;
    }

    /// <summary>
    /// The retiring player's identifier.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The player's name.
    /// </summary>
    public PlayerName PlayerName { get; }

    /// <summary>
    /// The player's last club before retirement.
    /// </summary>
    public ClubId LastClubId { get; }

    /// <summary>
    /// The date of retirement.
    /// </summary>
    public DateOnly RetirementDate { get; }

    /// <summary>
    /// The transfer that triggered this retirement event.
    /// </summary>
    public TransferId TriggeringTransferId { get; }

    /// <summary>
    /// When the retirement was recorded.
    /// </summary>
    public DateTimeOffset RetiredAt { get; }

    /// <summary>
    /// Creates a PlayerRetired event.
    /// </summary>
    public static PlayerRetired Create(
        PlayerId playerId,
        PlayerName playerName,
        ClubId lastClubId,
        DateOnly retirementDate,
        TransferId triggeringTransferId,
        DateTimeOffset? retiredAt = null
    ) => new(
        playerId,
        playerName,
        lastClubId,
        retirementDate,
        triggeringTransferId,
        retiredAt ?? DateTimeOffset.UtcNow
    );
}
