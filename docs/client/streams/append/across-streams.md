---
order: 2
title: Across streams
---

# Append across streams

<CopyMarkdownButton />

`AppendRecords` writes records across one or more streams in a single atomic transaction. Unlike [`AppendStream`](to-one-stream.md), each record carries its own destination, and pre-commit assertions can guard the state of any affected stream. Either the whole batch lands or none of it does.

Use this when an invariant spans more than one stream, for example "only emit a transfer event if the inventory stream has not moved since I read it".

## Append to multiple streams

Each record names its target stream. `ConsistencyCheck` factories attach assertions evaluated against the current state of any stream the transaction touches.

```csharp{4,5}
var transferEvent = new StockTransferred(sku, quantity, fromWarehouse, toWarehouse, DateTimeOffset.UtcNow);

var records = new[] {
    new AppendRecord { Stream = $"inventory-{sku}", Value = transferEvent },
    new AppendRecord { Stream = "transfer-log",     Value = transferEvent }
};

await client.Streams.AppendRecords(
    records,
    new AppendRecordsOptions {
        Checks = [ConsistencyCheck.StreamExists($"inventory-{sku}")]
    },
    default);
```

The request commits if and only if every check passes against the actual stream state at commit time.

## Guard cross-stream consistency

Each consistency check is a pre-commit assertion the server evaluates against the current state of a stream. If any check fails, the whole transaction is rejected. `ExpectedStreamState` on a single-stream [`AppendStream`](to-one-stream.md) is sugar for the same mechanism applied to one stream.

The most useful check is `StreamAtRevision`, which asserts a stream has not moved since you last read it. Pair it with a write to a different stream to enforce a multi-aggregate invariant atomically. Below, a transfer event is written only if the inventory stream is still at the revision that was read:

```csharp
var inventory = await client.Streams.LastRecord($"inventory-{sku}", skipDecoding: true);

var transferEvent = new StockTransferred(sku, quantity, fromWarehouse, toWarehouse, DateTimeOffset.UtcNow);

await client.Streams.AppendRecords(
    [new AppendRecord { Stream = "transfer-log", Value = transferEvent }],
    new AppendRecordsOptions {
        Checks = [ConsistencyCheck.StreamAtRevision($"inventory-{sku}", inventory.Revision)]
    },
    default);
```

Combine several checks in one request with `AddChecks`. Each check targets a stream independently, so revision pins and existence assertions can mix freely:

```csharp
await client.Streams.AppendRecords(
    [new AppendRecord { Stream = $"order-{orderId}", Value = orderEvent }],
    new AppendRecordsOptions {
        Checks = [
            ConsistencyCheck.StreamAtRevision($"cart-{cartId}", cartRevision),
            ConsistencyCheck.StreamDoesNotExist($"order-{orderId}")
        ]
    },
    default);
```

Prefer fluent composition? `new AppendRecordsOptions().AddChecks(...)` returns a copy with the checks appended — chain it to add checks a call at a time.

Available checks:

| Factory                              | Asserts                                         |
|--------------------------------------|-------------------------------------------------|
| `StreamAtRevision(stream, revision)` | Current revision matches exactly                |
| `StreamExists(stream)`               | Stream has at least one record                  |
| `StreamDoesNotExist(stream)`         | Stream has no records, or has been deleted      |
| `StreamIsDeleted(stream)`            | Stream is soft-deleted                          |
| `StreamIsTombstoned(stream)`         | Stream is permanently deleted                   |

## Handle violations

A single `AppendRecords` request can include checks against several streams, so failures cannot collapse into one typed exception. `AppendConsistencyViolationException` carries every violation, each with the stream, expected state, and actual state:

```csharp
try {
    await client.Streams.AppendRecords(records, options, default);
}
catch (AppendConsistencyViolationException ex) {
    foreach (var violation in ex.Violations) {
        var state = violation.AsStreamStateViolation;
        Console.WriteLine(
            "Stream {0} expected {1}, was {2}",
            state.Stream, state.ExpectedState, state.ActualState);
    }
}
```

Size limits surface as `MaxAppendRecordSizeExceededException` for a single oversized record and `MaxAppendTransactionSizeExceededException` for a batch that exceeds the server limit.

See [Error handling](../../error-handling.md) for the full throwing model, validation, and cancellation.

## Reference

Each cross-stream check is read-only against another stream in the transaction. The check rules differ slightly from the [target stream rules in `AppendStream`](to-one-stream.md#append-target-state-matrix): `NoStream` and `Any` are also satisfied when the stream has been deleted or tombstoned, because the check asserts absence rather than guarding a write.

### Cross-stream check state matrix

<table>
<thead>
<tr><th rowspan="2">Expected</th><th colspan="4" align="center">Actual</th></tr>
<tr><th align="center">Revision M</th><th align="center">No stream</th><th align="center">Deleted</th><th align="center">Tombstoned</th></tr>
</thead>
<tbody>
<tr><td><code>Revision.From(N)</code></td><td align="center">✅ if M = N</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
<tr><td><code>NoStream</code></td><td align="center">❌</td><td align="center">✅</td><td align="center">✅</td><td align="center">✅</td></tr>
<tr><td><code>Any</code></td><td align="center">✅</td><td align="center">✅</td><td align="center">✅</td><td align="center">✅</td></tr>
<tr><td><code>Exists</code></td><td align="center">✅</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
<tr><td><code>Deleted</code></td><td align="center">❌</td><td align="center">❌</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Tombstoned</code></td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td><td align="center">✅</td></tr>
</tbody>
</table>

### Append-specific exceptions

| Exception                                   | Cause                                                       |
|---------------------------------------------|-------------------------------------------------------------|
| `AppendConsistencyViolationException`       | One or more `ConsistencyCheck` failed                       |
| `MaxAppendRecordSizeExceededException`      | A single record exceeds the server size limit               |
| `MaxAppendTransactionSizeExceededException` | The transaction exceeds the server size limit               |
