namespace Transfermarkt.Domain;

/// <summary>
/// A period of time a player spent at a club.
/// </summary>
[PublicAPI]
public sealed record CareerSpell {
    CareerSpell(
        ClubId clubId,
        string clubName,
        DateOnly startDate,
        DateOnly endDate,
        TransferType howJoined,
        Money transferFee
    ) {
        ClubId      = clubId;
        ClubName    = clubName;
        StartDate   = startDate;
        EndDate     = endDate;
        HowJoined   = howJoined;
        TransferFee = transferFee;
    }

    /// <summary>
    /// The club where this spell occurred.
    /// </summary>
    public ClubId ClubId { get; }

    /// <summary>
    /// The club's name.
    /// </summary>
    public string ClubName { get; }

    /// <summary>
    /// When the player joined this club.
    /// </summary>
    public DateOnly StartDate { get; }

    /// <summary>
    /// When the player left this club. <see cref="DateOnly.MinValue"/> if still at club.
    /// </summary>
    public DateOnly EndDate { get; }

    /// <summary>
    /// Whether the player is currently at this club.
    /// </summary>
    public bool IsCurrent => EndDate == DateOnly.MinValue;

    /// <summary>
    /// How the player joined this club.
    /// </summary>
    public TransferType HowJoined { get; }

    /// <summary>
    /// The fee paid. <see cref="Money.Zero"/> for free transfers.
    /// </summary>
    public Money TransferFee { get; }

    /// <summary>
    /// Creates a CareerSpell.
    /// </summary>
    public static CareerSpell Create(
        ClubId clubId,
        string clubName,
        DateOnly startDate,
        TransferType howJoined,
        DateOnly? endDate = null,
        Money? transferFee = null
    ) => new(
        clubId,
        clubName,
        startDate,
        endDate ?? DateOnly.MinValue,
        howJoined,
        transferFee ?? Money.Zero
    );
}
