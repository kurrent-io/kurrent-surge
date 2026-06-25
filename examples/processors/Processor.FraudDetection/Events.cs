namespace Processor.FraudDetection.Events;

/// <summary>
/// Event representing a financial transaction.
/// </summary>
/// <param name="TransactionId">Unique identifier for this transaction.</param>
/// <param name="AccountId">Account that initiated the transaction.</param>
/// <param name="Amount">Transaction amount in dollars.</param>
/// <param name="Location">Geographic location where transaction occurred.</param>
/// <param name="Timestamp">When the transaction occurred.</param>
public record Transaction(
    string TransactionId,
    string AccountId,
    decimal Amount,
    string Location,
    DateTimeOffset Timestamp
);
