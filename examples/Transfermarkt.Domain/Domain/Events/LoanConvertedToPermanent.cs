using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Events;

/// <summary>
/// A loan has been converted to a permanent transfer.
/// </summary>
/// <remarks>
/// SAGA PATTERN: Emitted when conditions are met (buy clause exercised,
/// obligation triggered). Links the original loan to the new permanent deal.
/// </remarks>
[PublicAPI]
public sealed record LoanConvertedToPermanent {
    /// <summary>
    /// The original loan transfer that is being converted.
    /// </summary>
    public TransferId OriginalLoanTransferId { get; init; }

    /// <summary>
    /// The new permanent transfer created.
    /// </summary>
    public TransferId NewPermanentTransferId { get; init; }

    /// <summary>
    /// The player involved in the conversion.
    /// </summary>
    public PlayerId PlayerId { get; init; }

    /// <summary>
    /// The fee paid for the permanent transfer.
    /// </summary>
    public Money Fee { get; init; }

    /// <summary>
    /// When the conversion was recorded.
    /// </summary>
    public DateTimeOffset ConvertedAt { get; init; }

}
