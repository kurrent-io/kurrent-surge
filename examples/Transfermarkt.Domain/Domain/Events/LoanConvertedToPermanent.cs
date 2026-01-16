namespace Transfermarkt.Domain;

/// <summary>
/// A loan has been converted to a permanent transfer.
/// </summary>
/// <remarks>
/// SAGA PATTERN: Emitted when conditions are met (buy clause exercised,
/// obligation triggered). Links the original loan to the new permanent deal.
/// </remarks>
[PublicAPI]
public sealed record LoanConvertedToPermanent {
    LoanConvertedToPermanent(
        TransferId originalLoanTransferId,
        TransferId newPermanentTransferId,
        PlayerId playerId,
        Money fee,
        DateTimeOffset convertedAt
    ) {
        OriginalLoanTransferId  = originalLoanTransferId;
        NewPermanentTransferId  = newPermanentTransferId;
        PlayerId                = playerId;
        Fee                     = fee;
        ConvertedAt             = convertedAt;
    }

    /// <summary>
    /// The original loan transfer that is being converted.
    /// </summary>
    public TransferId OriginalLoanTransferId { get; }

    /// <summary>
    /// The new permanent transfer created.
    /// </summary>
    public TransferId NewPermanentTransferId { get; }

    /// <summary>
    /// The player involved in the conversion.
    /// </summary>
    public PlayerId PlayerId { get; }

    /// <summary>
    /// The fee paid for the permanent transfer.
    /// </summary>
    public Money Fee { get; }

    /// <summary>
    /// When the conversion was recorded.
    /// </summary>
    public DateTimeOffset ConvertedAt { get; }

    /// <summary>
    /// Creates a LoanConvertedToPermanent event.
    /// </summary>
    public static LoanConvertedToPermanent Create(
        TransferId originalLoanTransferId,
        TransferId newPermanentTransferId,
        PlayerId playerId,
        Money fee,
        DateTimeOffset? convertedAt = null
    ) => new(
        originalLoanTransferId,
        newPermanentTransferId,
        playerId,
        fee,
        convertedAt ?? DateTimeOffset.UtcNow
    );
}
