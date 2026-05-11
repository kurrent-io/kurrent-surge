---
order: 1
---

# Append

<CopyMarkdownButton />

Streams hold an ordered sequence of records. Use `AppendStream` to write to a single stream and `AppendRecords` to write across several streams in one atomic transaction.

## Append a record

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

Payloads serialize as JSON by default. The stream is created on first append.

The response carries the target stream, the new revision, and the log position of the last appended record.

## Append a batch atomically

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

## Set ids, metadata, and timestamps

Build a full record when you need a deterministic id, custom metadata, or an explicit timestamp:

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

Throws `StreamAlreadyExistsException` if the stream already has records. Use this for creation events that must be unique.

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

## Append to multiple streams in one transaction

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

Available checks: `StreamAtRevision`, `StreamExists`, `StreamDoesNotExist`, `StreamIsDeleted`, `StreamIsTombstoned`.

## Append non-JSON payloads

Override the format per record with `WithDataFormat`.

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

## Stream state matrix

Every append compares an *expected* state against the *actual* state of the stream and rejects the operation on a mismatch. The rules differ slightly for the stream being appended to versus read-only consistency checks on other streams. In the matrices below, ✅ means the check passes and ❌ means the operation is rejected with a stream state violation.

### Append target

Applied to the stream being appended to. A tombstoned stream rejects every append.

| Expected ↓ / Actual → | Revision M  | No stream | Deleted | Tombstoned |
|:----------------------|:-----------:|:---------:|:-------:|:----------:|
| `Revision.From(N)`    | ✅ if M = N  |     ❌     |    ❌    |     ❌      |
| `NoStream`            |      ❌      |     ✅     |    ✅    |     ❌      |
| `Any`                 |      ✅      |     ✅     |    ✅    |     ❌      |
| `Exists`              |      ✅      |     ❌     |    ❌    |     ❌      |
| `Deleted`             |      ❌      |     ❌     |    ✅    |     ❌      |
| `Tombstoned`          |      ❌      |     ❌     |    ❌    |     ❌      |

### Cross-stream consistency check

Applied as a read-only check on another stream in an `AppendRecords` transaction. `NoStream` and `Any` are also satisfied when the stream has been deleted or tombstoned, because the check is asserting absence rather than guarding a write.

| Expected ↓ / Actual → | Revision M  | No stream | Deleted | Tombstoned |
|:----------------------|:-----------:|:---------:|:-------:|:----------:|
| `Revision.From(N)`    | ✅ if M = N  |     ❌     |    ❌    |     ❌      |
| `NoStream`            |      ❌      |     ✅     |    ✅    |     ✅      |
| `Any`                 |      ✅      |     ✅     |    ✅    |     ✅      |
| `Exists`              |      ✅      |     ❌     |    ❌    |     ❌      |
| `Deleted`             |      ❌      |     ❌     |    ✅    |     ❌      |
| `Tombstoned`          |      ❌      |     ❌     |    ❌    |     ✅      |

See [Append-specific exceptions](#reference) below for the exception raised on each rejection.

## Reference

`AppendRecord.New` builder methods:

| Method                              | Description                                                            |
|-------------------------------------|------------------------------------------------------------------------|
| `ForStream(StreamName)`             | Set the destination stream                                             |
| `WithId(Guid)`                      | Set a specific record id (auto-generated if omitted)                   |
| `WithValue(object)`                 | Set the payload                                                        |
| `WithDataFormat(SchemaDataFormat)`  | Override serialization format (`Json`, `Protobuf`, `Avro`, `Bytes`)    |
| `WithMetadata(Metadata)`            | Replace metadata wholesale                                             |
| `WithMetadata<T>(string, T)`        | Add or update a single metadata entry                                  |
| `WithTimestamp(DateTimeOffset)`     | Override the record timestamp                                          |

`Build()` throws `ValidationException` if the value is missing. The destination stream is enforced when the record joins a request, so a record without `ForStream` is valid as long as the request supplies one.

Expected stream state values:

| Value              | Check performed                    |
|--------------------|------------------------------------|
| `Any`              | None, always succeeds              |
| `NoStream`         | Stream must not exist              |
| `Exists`           | Stream must exist                  |
| `Revision.From(n)` | Stream revision must match exactly |

Append-specific exceptions:

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
