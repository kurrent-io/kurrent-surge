# Processor.FraudDetection

Real-time fraud detection using pattern matching on transaction sequences.

## What You'll Learn

- Complex Event Processing (CEP) patterns
- Time-windowed pattern matching
- State management for fraud detection
- Multi-step pattern recognition

## How It Works

1. **Input**: `Transaction` events from payment processing
2. **Pattern**: Small transaction (≤$1) followed by large transaction (≥$500) within 1 minute
3. **Logic**: This detects the classic "card testing" fraud pattern where criminals:
   - First test a stolen card with a small purchase
   - If successful, immediately make a large fraudulent purchase
4. **Output**: Log fraud alerts when pattern is detected

```
Transaction(acct1, $0.50, NYC, 10:00:00)   → Track small tx
Transaction(acct1, $750.00, NYC, 10:00:45) → 🚨 FRAUD ALERT!
                                             Pattern: $0.50 → $750 in 45s
```

## Prerequisites

- KurrentDB running on `localhost:2113`

## Running the Example

```bash
cd examples/processors/Processor.FraudDetection
dotnet run
```

A hosted `TransactionSeeder` writes transactions for four accounts, one of which repeats the
card-testing pattern, so alerts appear within seconds. Press `Ctrl+C` to stop.

## Key Code

```csharp
void ProcessTransaction(Transaction tx, ILogger logger) {
    var state = _accountStates.GetOrAdd(tx.AccountId, _ => new AccountState());
    state.CleanupExpiredTransactions(tx.Timestamp, PatternWindow);

    if (tx.Amount <= SmallTransactionThreshold) {
        // Track small transaction as potential probe
        state.RecordSmallTransaction(tx);
    } else if (tx.Amount >= LargeTransactionThreshold) {
        // Check for matching small tx in the pattern window
        var matchingSmallTx = state.FindMatchingSmallTransaction(tx.Timestamp, PatternWindow);
        if (matchingSmallTx != null) {
            RaiseFraudAlert(matchingSmallTx, tx, logger);
        }
    }
}
```

## Fraud Pattern

| Step | Transaction | Detection |
|------|-------------|-----------|
| 1 | Small amount (≤$1) | Track as potential probe |
| 2 | Large amount (≥$500) within 60s | Pattern matched → Alert! |

## Additional Signals

- **Location mismatch**: Different locations between small and large tx
- **Velocity**: Multiple fraud patterns from same account

## Sample Output

```
[INF] Pattern: Small tx (≤$1) → Large tx (≥$500) within 00:01:00
[WRN] 🚨 FRAUD ALERT | Account: acct-789 | Pattern: $0.50 → $1,250.00 in 32.5s
[WRN]    └─ Small tx: tx-001 at New York, NY (2024-01-15 10:00:00)
[WRN]    └─ Large tx: tx-002 at Los Angeles, CA (2024-01-15 10:00:32)
[WRN]    └─ ⚠️ LOCATION MISMATCH: New York, NY → Los Angeles, CA
[INF] Checkpoint #1 | Transactions: 523 | Accounts: 89 | Fraud Alerts: 3
```

## Original Tutorials

- [Apache Flink Fraud Detection Tutorial](https://nightlies.apache.org/flink/flink-docs-master/docs/try-flink/datastream/)
- [Flink CEP Library](https://nightlies.apache.org/flink/flink-docs-stable/docs/libs/cep/)
