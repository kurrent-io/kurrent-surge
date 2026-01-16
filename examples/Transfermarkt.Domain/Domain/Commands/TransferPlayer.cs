namespace Transfermarkt.Domain;

/// <summary>
/// Command to transfer a player between clubs.
/// </summary>
[PublicAPI]
public sealed record TransferPlayer {
    TransferPlayer(
        PlayerId playerId,
        ClubId fromClubId,
        ClubId toClubId,
        TransferType type,
        DateOnly effectiveDate,
        Money fee,
        LoanTerms loanTerms
    ) {
        PlayerId      = playerId;
        FromClubId    = fromClubId;
        ToClubId      = toClubId;
        Type          = type;
        EffectiveDate = effectiveDate;
        Fee           = fee;
        LoanTerms     = loanTerms;
    }

    /// <summary>
    /// The player being transferred.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The club the player is leaving. <see cref="ClubId.None"/> for new registrations.
    /// </summary>
    public ClubId FromClubId { get; }

    /// <summary>
    /// The club the player is joining. <see cref="ClubId.None"/> for retirements.
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
    /// Creates a TransferPlayer command.
    /// </summary>
    public static TransferPlayer Create(
        PlayerId playerId,
        ClubId fromClubId,
        ClubId toClubId,
        TransferType type,
        DateOnly effectiveDate,
        Money? fee = null,
        LoanTerms? loanTerms = null
    ) => new(
        playerId,
        fromClubId,
        toClubId,
        type,
        effectiveDate,
        fee ?? Money.Zero,
        loanTerms ?? LoanTerms.None
    );
}
