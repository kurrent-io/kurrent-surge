namespace Transfermarkt.Domain;

/// <summary>
/// Command to return a player from loan to their parent club.
/// </summary>
[PublicAPI]
public sealed record ReturnFromLoan {
    ReturnFromLoan(TransferId originalLoanTransferId, DateOnly returnDate) {
        OriginalLoanTransferId = originalLoanTransferId;
        ReturnDate             = returnDate;
    }

    /// <summary>
    /// The original loan transfer.
    /// </summary>
    public TransferId OriginalLoanTransferId { get; }

    /// <summary>
    /// The date the player returns to their parent club.
    /// </summary>
    public DateOnly ReturnDate { get; }

    /// <summary>
    /// Creates a ReturnFromLoan command.
    /// </summary>
    public static ReturnFromLoan Create(TransferId originalLoanTransferId, DateOnly returnDate) =>
        new(originalLoanTransferId, returnDate);
}
