using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Commands;

/// <summary>
/// Command to return a player from loan to their parent club.
/// </summary>
[PublicAPI]
public sealed record ReturnFromLoan {
    /// <summary>
    /// The original loan transfer.
    /// </summary>
    public TransferId OriginalLoanTransferId { get; init; }

    /// <summary>
    /// The date the player returns to their parent club.
    /// </summary>
    public DateOnly ReturnDate { get; init; }
}
