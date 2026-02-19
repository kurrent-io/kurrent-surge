namespace Transfermarkt.Domain;

/// <summary>
/// Command to transfer a player between clubs.
/// </summary>
[PublicAPI]
public sealed record TransferPlayer {
    /// <summary>
    /// The player being transferred.
    /// </summary>
    public PlayerId PlayerId { get; init; }

    /// <summary>
    /// The club the player is leaving. <see cref="ClubId.None"/> for new registrations.
    /// </summary>
    public ClubId FromClubId { get; init; }

    /// <summary>
    /// The club the player is joining. <see cref="ClubId.None"/> for retirements.
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
}
