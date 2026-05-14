---
order: 1
---

# Append

<CopyMarkdownButton />

Streams hold an ordered sequence of records. Use `AppendStream` to write to a single stream and `AppendRecords` to write across several streams in one atomic transaction.

## Append a single record

Build a record and pass it to `AppendStream`:

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new { Product = "Widget", Quantity = 5 })
    .Build();

var result = await client.Streams.AppendStream(record);

Console.WriteLine("Stream:   {0}", result.Stream);
Console.WriteLine("Revision: {0}", result.Revision);
Console.WriteLine("Position: {0}", result.Position);
```

Payloads serialize as JSON by default. The stream is created on first append. The same builder works for events, commands, snapshots, or any other payload shape.

The response carries the target stream, the new revision, and the log position of the last appended record.

## Append a batch to one stream

`AppendStreamRequest.For(stream)` collects multiple records destined for the same stream. They commit together or not at all.

```csharp
var request = AppendStreamRequest.For("order-456")
    .WithMessage(new { Product = "Gadget", Quantity = 2 })
    .WithMessage(new { ConfirmedAt = DateTimeOffset.UtcNow })
    .WithMessage(new { TrackingId = "TRK-789" })
    .Build();

var result = await client.Streams.AppendStream(request);
```

`WithMessage` is the payload-only shortcut. Reach for `WithRecord` when you need to control more than just the payload.

## Attach metadata to a record

Use the full record builder when you need more control than `WithMessage` provides:

```csharp
var request = AppendStreamRequest.For("order-456")
    .WithRecord(AppendRecord.New
        .WithId(Guid.NewGuid())
        .WithValue(new { Product = "Gadget" })
        .WithMetadata("correlation-id", correlationId)
        .WithMetadata("causation-id", causationId)
        .WithTimestamp(DateTimeOffset.UtcNow)
        .Build())
    .Build();
```

The destination stream comes from the request, so the record itself does not need `ForStream`.

## Guard against concurrent writes

`ExpectedStreamState` tells the server what state the stream must be in before the append is accepted. The append is rejected when the actual state differs.

::: tabs#expected-state

@tab Any state

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.Any)
    .WithMessage(payload)
    .Build();
```

The default. The append succeeds whether or not the stream exists.

@tab Stream must not exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.NoStream)
    .WithMessage(new { Product = "Widget", Quantity = 5 })
    .Build();
```

Throws `StreamAlreadyExistsException` if the stream already has records. Use this for the first record on a stream that must not collide.

@tab Stream must exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.Exists)
    .WithMessage(payload)
    .Build();
```

Throws `StreamNotFoundException` when the stream does not exist.

@tab Match revision

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(Revision.From(4))
    .WithMessage(payload)
    .Build();
```

Throws `StreamRevisionConflictException` if the current revision is anything other than 4. This is the strongest guarantee, intended for writers contending on the same stream.

The usual pattern is to read the stream's current revision and append against it:

```csharp{1,4}
var last = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Backwards, skipDecoding: true, cancellationToken: default);

var request = AppendStreamRequest.For("order-123")
    .ExpectingState(last.Revision)
    .WithMessage(payload)
    .Build();

var result = await client.Streams.AppendStream(request);
```

`Revision` converts implicitly to `ExpectedStreamState`, so passing `last.Revision` works directly.

:::

## Append across multiple streams

`AppendRecords` writes to several streams atomically. Each record carries its own destination, and `ConsistencyCheck` factories attach pre-commit checks evaluated against the current state of the affected streams.

```csharp{5,6}
var transferEvent = new StockTransferred(sku, quantity, fromWarehouse, toWarehouse, DateTimeOffset.UtcNow);

await client.Streams.AppendRecords(
    AppendRecordsRequest.New()
        .WithRecord(transferEvent, $"inventory-{sku}")
        .WithRecord(transferEvent, "transfer-log")
        .WithChecks(ConsistencyCheck.StreamExists($"inventory-{sku}"))
        .Build());
```

See [Guard cross-stream consistency](#guard-cross-stream-consistency) for the full set of checks and how violations surface.

## Guard cross-stream consistency

Each consistency check is a pre-commit assertion the server evaluates against the current state of a stream. If any check on an `AppendRecords` request fails, the whole transaction is rejected. `ExpectingState` on a single-stream `AppendStream` is sugar for the same mechanism, applied to one stream.

The most useful check is `StreamAtRevision`, which asserts a stream has not moved since you last read it. Pair it with a write to a different stream to enforce a multi-aggregate invariant atomically. Below, a transfer event is written only if the inventory stream is still at the revision that was read:

```csharp
var inventory = await client.Streams.ReadStreamEdge($"inventory-{sku}", ReadDirection.Backwards, skipDecoding: true, cancellationToken: default);

var transferEvent = new StockTransferred(sku, quantity, fromWarehouse, toWarehouse, DateTimeOffset.UtcNow);

await client.Streams.AppendRecords(
    AppendRecordsRequest.New()
        .WithRecord(transferEvent, "transfer-log")
        .WithChecks(ConsistencyCheck.StreamAtRevision($"inventory-{sku}", inventory.Revision))
        .Build());
```

Combine several checks in one request with `WithChecks`. Each check targets a stream independently, so revision pins and existence assertions can mix freely:

```csharp
await client.Streams.AppendRecords(
    AppendRecordsRequest.New()
        .WithRecord(orderEvent, $"order-{orderId}")
        .WithChecks(
            ConsistencyCheck.StreamAtRevision($"cart-{cartId}", cartRevision),
            ConsistencyCheck.StreamDoesNotExist($"order-{orderId}"))
        .Build());
```

`WithCheck` adds a single check if you prefer one-at-a-time composition.

Available checks:

| Factory                              | Asserts                                         |
|--------------------------------------|-------------------------------------------------|
| `StreamAtRevision(stream, revision)` | Current revision matches exactly                |
| `StreamExists(stream)`               | Stream has at least one record                  |
| `StreamDoesNotExist(stream)`         | Stream has no records, or has been deleted      |
| `StreamIsDeleted(stream)`            | Stream is soft-deleted                          |
| `StreamIsTombstoned(stream)`         | Stream is permanently deleted                   |

When a check fails, `AppendRecords` throws `AppendConsistencyViolationException` carrying one violation per failed check, each with the stream, expected state, and actual state. See [Handle append errors](#handle-append-errors) for the catch pattern, and the [cross-stream check state matrix](#cross-stream-check-state-matrix) for which expected and actual combinations pass.

## Choose a payload format

JSON is the default. Override per record with `WithDataFormat`.

::: tabs#data-formats

@tab JSON

The default. No format call required.

```csharp
var jsonRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPayload)
    .Build();
```

@tab Bytes

For payloads you have already serialized.

```csharp
var bytesRecord = AppendRecord.New
    .ForStream("blobs")
    .WithValue(rawBytes)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();
```

@tab Protobuf

For messages generated from `.proto` definitions.

```csharp
var protoRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(protobufMessage)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();
```

@tab Avro

For Avro-encoded payloads.

```csharp
var avroRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(avroPayload)
    .WithDataFormat(SchemaDataFormat.Avro)
    .Build();
```

:::

## Handle append errors

`AppendStream` throws when the server rejects the append. The exception type tells you why. Concurrency failures carry the stream and, where relevant, both the expected and actual revision.

```csharp
try {
    await client.Streams.AppendStream(record, Revision.From(4));
}
catch (StreamRevisionConflictException ex) {
    Console.WriteLine("Expected {0}, was {1}", ex.ExpectedRevision, ex.ActualRevision);
}
```

`AppendRecords` is different. A single request can include checks against several streams, so it cannot collapse failures into one typed exception. Instead it throws `AppendConsistencyViolationException` carrying every violation:

```csharp
try {
    await client.Streams.AppendRecords(request);
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

See [Error handling](../error-handling.md) for the full throwing model, validation, and cancellation.

## Reference

Every append compares an *expected* state against the *actual* state of the stream and rejects the operation on a mismatch. The rules differ slightly for the stream being appended to versus read-only consistency checks on other streams. ✅ means the check passes, and ❌ means the operation is rejected with a stream state violation.

### Expected stream state values

| Value              | Check performed                    |
|--------------------|------------------------------------|
| `Any`              | None, always succeeds              |
| `NoStream`         | Stream must not exist              |
| `Exists`           | Stream must exist                  |
| `Revision.From(n)` | Stream revision must match exactly |

### Append target state matrix

Applied to the stream being appended to. A tombstoned stream rejects every append.

<table>
<thead>
<tr><th rowspan="2">Expected</th><th colspan="4" align="center">Actual</th></tr>
<tr><th align="center">Revision M</th><th align="center">No stream</th><th align="center">Deleted</th><th align="center">Tombstoned</th></tr>
</thead>
<tbody>
<tr><td><code>Revision.From(N)</code></td><td align="center">✅ if M = N</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
<tr><td><code>NoStream</code></td><td align="center">❌</td><td align="center">✅</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Any</code></td><td align="center">✅</td><td align="center">✅</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Exists</code></td><td align="center">✅</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
<tr><td><code>Deleted</code></td><td align="center">❌</td><td align="center">❌</td><td align="center">✅</td><td align="center">❌</td></tr>
<tr><td><code>Tombstoned</code></td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td><td align="center">❌</td></tr>
</tbody>
</table>

### Cross-stream check state matrix

Applied as a read-only check on another stream in an `AppendRecords` transaction. `NoStream` and `Any` are also satisfied when the stream has been deleted or tombstoned, because the check is asserting absence rather than guarding a write.

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
| `StreamNotFoundException`                   | `Exists` was expected, stream does not exist                |
| `StreamAlreadyExistsException`              | `NoStream` was expected, stream already has records         |
| `StreamDeletedException`                    | Stream was soft-deleted                                     |
| `StreamTombstonedException`                 | Stream was permanently deleted                              |
| `StreamRevisionConflictException`           | Exact revision was expected, current revision differs       |
| `MaxAppendRecordSizeExceededException`      | A single record exceeds the server size limit               |
| `MaxAppendTransactionSizeExceededException` | The transaction exceeds the server size limit               |
| `AppendConsistencyViolationException`       | One or more `ConsistencyCheck` failed in `AppendRecords`    |
