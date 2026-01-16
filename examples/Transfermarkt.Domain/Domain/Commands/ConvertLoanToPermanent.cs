namespace Transfermarkt.Domain;

/// <summary>
/// Command to convert an existing loan to a permanent transfer.
/// </summary>
[PublicAPI]
public sealed record ConvertLoanToPermanent {
    ConvertLoanToPermanent(
        TransferId originalLoanTransferId,
        Money fee,
        DateOnly effectiveDate
    ) {
        OriginalLoanTransferId = originalLoanTransferId;
        Fee                    = fee;
        EffectiveDate          = effectiveDate;
    }

    /// <summary>
    /// The original loan transfer being converted.
    /// </summary>
    public TransferId OriginalLoanTransferId { get; }

    /// <summary>
    /// The fee paid for the permanent transfer.
    /// </summary>
    public Money Fee { get; }

    /// <summary>
    /// The date the permanent transfer becomes effective.
    /// </summary>
    public DateOnly EffectiveDate { get; }

    /// <summary>
    /// Creates a ConvertLoanToPermanent command.
    /// </summary>
    public static ConvertLoanToPermanent Create(
        TransferId originalLoanTransferId,
        Money fee,
        DateOnly effectiveDate
    ) => new(originalLoanTransferId, fee, effectiveDate);
}
