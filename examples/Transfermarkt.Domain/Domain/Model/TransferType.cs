namespace Transfermarkt.Domain;

/// <summary>
/// Type of transfer movement.
/// </summary>
public enum TransferType {
    /// <summary>
    /// Player enters the system (youth promotion, first pro contract).
    /// </summary>
    Registration,

    /// <summary>
    /// Permanent transfer with or without fee.
    /// </summary>
    Permanent,

    /// <summary>
    /// Temporary move with optional buy clause.
    /// </summary>
    Loan,

    /// <summary>
    /// Return to parent club after loan.
    /// </summary>
    LoanReturn,

    /// <summary>
    /// Loan converted to permanent transfer.
    /// </summary>
    LoanToPermanent,

    /// <summary>
    /// Out of contract move (no fee).
    /// </summary>
    FreeTransfer,

    /// <summary>
    /// Player exits the system (retirement).
    /// </summary>
    Retirement
}
