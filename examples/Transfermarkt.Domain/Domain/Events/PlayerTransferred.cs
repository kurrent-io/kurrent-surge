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
    /// <summary>
    /// Unique identifier for this transfer.
    /// </summary>
    public TransferId TransferId { get; init; }

    /// <summary>
    /// The player being transferred.
    /// </summary>
    public PlayerId PlayerId { get; init; }

    /// <summary>
    /// The player's name at the time of transfer.
    /// </summary>
    public PlayerName PlayerName { get; init; }

    /// <summary>
    /// The club the player is leaving. <see cref="ClubId.None"/> if entering the system.
    /// </summary>
    public ClubId FromClubId { get; init; }

    /// <summary>
    /// The club the player is joining. <see cref="ClubId.None"/> if leaving the system (retirement).
    /// </summary>
    public ClubId ToClubId { get; init; }

    /// <summary>
    /// The type of transfer.
    /// </summary>
    public TransferType Type { get; init; }

    /// <summary>
    /// The date the transfer becomes effective.
    /// </summary>
    public DateOnly EffectiveDate { get; init; }

    /// <summary>
    /// The transfer fee. <see cref="Money.Zero"/> for free transfers or loans without fee.
    /// </summary>
    public Money Fee { get; init; }

    /// <summary>
    /// Loan terms if this is a loan transfer. <see cref="LoanTerms.None"/> for non-loan transfers.
    /// </summary>
    public LoanTerms LoanTerms { get; init; }

    /// <summary>
    /// When the transfer was recorded.
    /// </summary>
    public DateTimeOffset TransferredAt { get; init; }

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

}
