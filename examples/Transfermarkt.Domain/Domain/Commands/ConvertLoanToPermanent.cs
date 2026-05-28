using Transfermarkt.Domain.Model;

namespace Transfermarkt.Domain.Commands;

/// <summary>
/// Command to convert an existing loan to a permanent transfer.
/// </summary>
[PublicAPI]
public sealed record ConvertLoanToPermanent {
    /// <summary>
    /// The original loan transfer being converted.
    /// </summary>
    public TransferId OriginalLoanTransferId { get; init; }

    /// <summary>
    /// The fee paid for the permanent transfer.
    /// </summary>
    public Money Fee { get; init; }

    /// <summary>
    /// The date the permanent transfer becomes effective.
    /// </summary>
    public DateOnly EffectiveDate { get; init; }
}
