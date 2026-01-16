namespace Transfermarkt.Domain;

/// <summary>
/// Net transfer spending for a club in a period.
/// </summary>
[PublicAPI]
public sealed record ClubSpendingProjection {
    ClubSpendingProjection(
        ClubId clubId,
        Season season,
        Money totalSpent,
        Money totalReceived,
        Money netSpend,
        int playersIn,
        int playersOut
    ) {
        ClubId        = clubId;
        Season        = season;
        TotalSpent    = totalSpent;
        TotalReceived = totalReceived;
        NetSpend      = netSpend;
        PlayersIn     = playersIn;
        PlayersOut    = playersOut;
    }

    /// <summary>
    /// The club this spending report belongs to.
    /// </summary>
    public ClubId ClubId { get; }

    /// <summary>
    /// The season this spending report covers.
    /// </summary>
    public Season Season { get; }

    /// <summary>
    /// Total spent on incoming transfers.
    /// </summary>
    public Money TotalSpent { get; }

    /// <summary>
    /// Total received from outgoing transfers.
    /// </summary>
    public Money TotalReceived { get; }

    /// <summary>
    /// Net spend (TotalSpent - TotalReceived).
    /// </summary>
    public Money NetSpend { get; }

    /// <summary>
    /// Number of players signed.
    /// </summary>
    public int PlayersIn { get; }

    /// <summary>
    /// Number of players sold/released.
    /// </summary>
    public int PlayersOut { get; }

    /// <summary>
    /// Creates a ClubSpendingProjection.
    /// </summary>
    public static ClubSpendingProjection Create(
        ClubId clubId,
        Season season,
        Money? totalSpent = null,
        Money? totalReceived = null,
        int playersIn = 0,
        int playersOut = 0
    ) {
        var spent    = totalSpent ?? Money.Zero;
        var received = totalReceived ?? Money.Zero;

        return new(
            clubId,
            season,
            spent,
            received,
            spent - received,
            playersIn,
            playersOut
        );
    }
}
