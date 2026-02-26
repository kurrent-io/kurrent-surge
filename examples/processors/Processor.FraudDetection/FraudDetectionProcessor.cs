using System.Collections.Concurrent;
using Kurrent.Processors;
using Microsoft.Extensions.Logging;
using Processor.FraudDetection.Events;

namespace Processor.FraudDetection;

/// <summary>
/// Fraud detection processor using pattern matching on transaction sequences.
/// Detects the classic "card testing" pattern: small transaction followed by large transaction.
/// Demonstrates CEP-style pattern matching in stream processing.
/// </summary>
public sealed class FraudDetectionProcessor : IRecordProcessor {
    const decimal SmallTransactionThreshold = 1.00m;   // Card testing probe
    const decimal LargeTransactionThreshold = 500.00m; // Large fraudulent purchase

    static readonly TimeSpan PatternWindow = TimeSpan.FromMinutes(1);

    readonly ConcurrentDictionary<string, AccountState> _accountStates = new();

    long _totalTransactions;
    long _fraudAlertsRaised;

    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is Transaction tx)
            ProcessTransaction(tx, context.Logger);

        return ValueTask.CompletedTask;
    }

    void ProcessTransaction(Transaction tx, ILogger logger) {
        _totalTransactions++;

        var state = _accountStates.GetOrAdd(tx.AccountId, _ => new AccountState());

        // Clean up old small transactions outside the pattern window
        state.CleanupExpiredTransactions(tx.Timestamp, PatternWindow);

        if (tx.Amount <= SmallTransactionThreshold) {
            // Small transaction - potential card test
            state.RecordSmallTransaction(tx);
            logger.LogDebug(
                "Small tx detected | Account: {Account} | Amount: ${Amount} | Tracking for pattern",
                tx.AccountId, tx.Amount
            );
        } else if (tx.Amount >= LargeTransactionThreshold) {
            // Large transaction - check for preceding small transaction
            var matchingSmallTx = state.FindMatchingSmallTransaction(tx.Timestamp, PatternWindow);

            if (matchingSmallTx != null)
                // FRAUD PATTERN DETECTED!
                RaiseFraudAlert(matchingSmallTx, tx, logger);
            else
                logger.LogDebug(
                    "Large tx | Account: {Account} | Amount: ${Amount} | No fraud pattern",
                    tx.AccountId, tx.Amount
                );
        }

        state.LastTransaction = tx;
    }

    void RaiseFraudAlert(Transaction smallTx, Transaction largeTx, ILogger logger) {
        _fraudAlertsRaised++;

        var timeBetween = largeTx.Timestamp - smallTx.Timestamp;

        logger.LogWarning(
            "🚨 FRAUD ALERT | Account: {Account} | Pattern: ${SmallAmount} → ${LargeAmount} in {Time:F1}s",
            largeTx.AccountId, smallTx.Amount, largeTx.Amount, timeBetween.TotalSeconds);

        logger.LogWarning(
            "   └─ Small tx: {SmallTxId} at {SmallLocation} ({SmallTime})",
            smallTx.TransactionId, smallTx.Location, smallTx.Timestamp);

        logger.LogWarning(
            "   └─ Large tx: {LargeTxId} at {LargeLocation} ({LargeTime})",
            largeTx.TransactionId, largeTx.Location, largeTx.Timestamp);

        // Additional location-based suspicion
        if (smallTx.Location != largeTx.Location)
            logger.LogWarning(
                "   └─ ⚠️ LOCATION MISMATCH: {Loc1} → {Loc2}",
                smallTx.Location, largeTx.Location);
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation(
            "Checkpoint {CheckpointId} | Transactions: {Tx} | Accounts: {Accounts} | Fraud Alerts: {Alerts}",
            context.CheckpointId, _totalTransactions, _accountStates.Count, _fraudAlertsRaised);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnComplete(CompleteContext context) {
        context.Logger.LogInformation(
            "Completed | Total transactions: {Tx}, Fraud alerts: {Alerts}",
            _totalTransactions, _fraudAlertsRaised);
        return ValueTask.CompletedTask;
    }

    sealed class AccountState {
        readonly List<Transaction> _recentSmallTransactions = [];

        public Transaction? LastTransaction { get; set; }

        public void RecordSmallTransaction(Transaction tx) =>
            _recentSmallTransactions.Add(tx);

        // Find a small transaction within the pattern window
        public Transaction? FindMatchingSmallTransaction(DateTimeOffset currentTime, TimeSpan window) =>
            _recentSmallTransactions
                .Where(tx => currentTime - tx.Timestamp <= window)
                .OrderByDescending(tx => tx.Timestamp)
                .FirstOrDefault();

        public void CleanupExpiredTransactions(DateTimeOffset currentTime, TimeSpan window) =>
            _recentSmallTransactions.RemoveAll(tx => currentTime - tx.Timestamp > window);
    }
}
