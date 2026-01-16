namespace Transfermarkt.Domain;

/// <summary>
/// A player moves between clubs (or enters/exits the system).
/// This is THE core event - equivalent to a financial transaction.
/// All other events derive from or react to this.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="FromClubId"/> is <see cref="ClubId.None"/>, the player is entering the system
/// (new registration, youth promotion).
/// </para>
/// <para>
/// When <see cref="ToClubId"/> is <see cref="ClubId.None"/>, the player is leaving the system
/// (retirement).
/// </para>
/// </remarks>
[PublicAPI]
public sealed record PlayerTransferred {
    PlayerTransferred(
        TransferId transferId,
        PlayerId playerId,
        PlayerName playerName,
        ClubId fromClubId,
        ClubId toClubId,
        TransferType type,
        DateOnly effectiveDate,
        Money fee,
        LoanTerms loanTerms,
        DateTimeOffset transferredAt
    ) {
        TransferId    = transferId;
        PlayerId      = playerId;
        PlayerName    = playerName;
        FromClubId    = fromClubId;
        ToClubId      = toClubId;
        Type          = type;
        EffectiveDate = effectiveDate;
        Fee           = fee;
        LoanTerms     = loanTerms;
        TransferredAt = transferredAt;
    }

    /// <summary>
    /// Unique identifier for this transfer.
    /// </summary>
    public TransferId TransferId { get; }

    /// <summary>
    /// The player being transferred.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The player's name at the time of transfer.
    /// </summary>
    public PlayerName PlayerName { get; }

    /// <summary>
    /// The club the player is leaving. <see cref="ClubId.None"/> if entering the system.
    /// </summary>
    public ClubId FromClubId { get; }

    /// <summary>
    /// The club the player is joining. <see cref="ClubId.None"/> if leaving the system (retirement).
    /// </summary>
    public ClubId ToClubId { get; }

    /// <summary>
    /// The type of transfer.
    /// </summary>
    public TransferType Type { get; }

    /// <summary>
    /// The date the transfer becomes effective.
    /// </summary>
    public DateOnly EffectiveDate { get; }

    /// <summary>
    /// The transfer fee. <see cref="Money.Zero"/> for free transfers or loans without fee.
    /// </summary>
    public Money Fee { get; }

    /// <summary>
    /// Loan terms if this is a loan transfer. <see cref="LoanTerms.None"/> for non-loan transfers.
    /// </summary>
    public LoanTerms LoanTerms { get; }

    /// <summary>
    /// When the transfer was recorded.
    /// </summary>
    public DateTimeOffset TransferredAt { get; }

    /// <summary>
    /// Whether the player is entering the system (new registration).
    /// </summary>
    public bool IsNewPlayer => FromClubId.IsUnset;

    /// <summary>
    /// Whether the player is leaving the system (retirement).
    /// </summary>
    public bool IsRetirement => ToClubId.IsUnset;

    /// <summary>
    /// Whether this is a loan transfer.
    /// </summary>
    public bool IsLoan => LoanTerms.IsSet;

    /// <summary>
    /// Creates a PlayerTransferred event.
    /// </summary>
    public static PlayerTransferred Create(
        TransferId transferId,
        PlayerId playerId,
        PlayerName playerName,
        ClubId fromClubId,
        ClubId toClubId,
        TransferType type,
        DateOnly effectiveDate,
        Money? fee = null,
        LoanTerms? loanTerms = null,
        DateTimeOffset? transferredAt = null
    ) => new(
        transferId,
        playerId,
        playerName,
        fromClubId,
        toClubId,
        type,
        effectiveDate,
        fee ?? Money.Zero,
        loanTerms ?? LoanTerms.None,
        transferredAt ?? DateTimeOffset.UtcNow
    );
}
