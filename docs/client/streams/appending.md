---
order: 1
---

# Appending records

<CopyMarkdownButton />

Records are the fundamental unit of data in KurrentDB. You append records to named streams, and each stream maintains an ordered sequence of records identified by a revision number.

## Append a single record

Build a record with the `AppendRecord.New` builder and pass it to `AppendStream`:

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new {
        Product  = "Widget",
        Quantity = 5
    })
    .Build();

var result = await client.Streams.AppendStream(record);

Console.WriteLine("Stream:   {0}", result.Stream);
Console.WriteLine("Revision: {0}", result.Revision);
Console.WriteLine("Position: {0}", result.Position);
```

`AppendStream` serializes the value as JSON by default. If the stream does not exist, it is created automatically.

The result is an `AppendStreamResponse` that exposes the target stream, the new stream revision, and the log position of the last appended record.

## Append multiple records

`AppendStreamRequest.For(stream)` builds a multi-record request. Records added to the request inherit the target stream automatically:

```csharp
var request = AppendStreamRequest.For("order-456")
    .WithMessage(new { Product = "Gadget", Quantity = 2 })
    .WithMessage(new { ConfirmedAt = DateTimeOffset.UtcNow })
    .WithMessage(new { TrackingId = "TRK-789" })
    .Build();

var result = await client.Streams.AppendStream(request);
```

All records in the request are appended atomically. They all succeed or all fail together.

`WithMessage` is a shortcut for the payload-only case. Use `WithRecord` when you need to set the id, timestamp, or richer metadata on the record itself:

```csharp{3,5-7}
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

## Optimistic concurrency

`ExpectedStreamState` controls optimistic concurrency. It tells the server what state the stream should be in before the append is accepted.

::: tabs#expected-state

@tab Any state

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.Any)
    .WithMessage(payload)
    .Build();
```

The append succeeds regardless of whether the stream exists or what revision it is at. Calls that omit `ExpectingState` default to this.

@tab Stream must not exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.NoStream)
    .WithMessage(new { Product = "Widget", Quantity = 5 })
    .Build();
```

The append fails with `StreamAlreadyExistsException` if the stream already has records. Use this for creation records where you need to guarantee uniqueness.

@tab Stream must exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.Exists)
    .WithMessage(payload)
    .Build();
```

The append fails with `StreamNotFoundException` if the stream does not exist.

@tab Match revision

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(Revision.From(4))
    .WithMessage(payload)
    .Build();
```

The append fails with `StreamRevisionConflictException` if the stream's current revision is not exactly 4. This is the strongest concurrency guarantee, intended for situations where multiple writers may contend on the same stream.

A typical pattern is to read the stream first, then append with the last known revision:

```csharp{1,4}
var last = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Backwards, skipDecoding: true, cancellationToken: default);

var request = AppendStreamRequest.For("order-123")
    .ExpectingState(last.Revision)
    .WithMessage(payload)
    .Build();

var result = await client.Streams.AppendStream(request);
```

`Revision` implicitly converts into `ExpectedStreamState`, so passing `last.Revision` directly works.

:::

| Value                            | Behavior                              |
|----------------------------------|---------------------------------------|
| `ExpectedStreamState.Any`        | No check, always succeeds             |
| `ExpectedStreamState.NoStream`   | Stream must not exist                 |
| `ExpectedStreamState.Exists`     | Stream must exist                     |
| `Revision.From(n)`               | Stream revision must match exactly    |

## Append across multiple streams

To append records to multiple streams in a single transaction, use `AppendRecords` with `AppendRecordsRequest.New()`:

```csharp{5,6}
var transferEvent = new StockTransferred(sku, quantity, fromWarehouse, toWarehouse, DateTimeOffset.UtcNow);

await client.Streams.AppendRecords(
    AppendRecordsRequest.New()
        .WithRecord(transferEvent, $"inventory-{sku}")
        .WithRecord(transferEvent, "transfer-log")
        .WithChecks(ConsistencyCheck.StreamExists($"inventory-{sku}"))
        .Build());
```

Each record carries its own stream. `ConsistencyCheck` factories (`StreamAtRevision`, `StreamExists`, `StreamDoesNotExist`, `StreamIsDeleted`, `StreamIsTombstoned`) attach pre-commit checks evaluated atomically with the append.

## Data formats

Payloads are serialized as JSON by default. Override the format per record with `WithDataFormat`.

::: tabs#data-formats

@tab JSON

JSON is the default. No explicit format call is required.

```csharp
var jsonRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPayload)
    .Build();
```

@tab Bytes

Use `SchemaDataFormat.Bytes` when the value is already serialized.

```csharp
var bytesRecord = AppendRecord.New
    .ForStream("blobs")
    .WithValue(rawBytes)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();
```

@tab Protobuf

Use `SchemaDataFormat.Protobuf` for messages generated from `.proto` definitions.

```csharp
var protoRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(protobufMessage)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();
```

@tab Avro

Use `SchemaDataFormat.Avro` for Avro-encoded payloads.

```csharp
var avroRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(avroPayload)
    .WithDataFormat(SchemaDataFormat.Avro)
    .Build();
```

:::

## Error handling

`AppendStream` throws when the server rejects the append. The exception type tells you why. See [Error handling](../error-handling.md) for the throwing model, validation, and cancellation.

A failed concurrency check raises a typed exception that carries the stream and, where relevant, both the expected and actual revision:

```csharp
try {
    await client.Streams.AppendStream(record, Revision.From(4));
}
catch (StreamRevisionConflictException ex) {
    Console.WriteLine("Expected {0}, was {1}", ex.ExpectedRevision, ex.ActualRevision);
}
```

Append-specific exceptions:

| Exception                                  | Cause                                                  |
|--------------------------------------------|--------------------------------------------------------|
| `StreamNotFoundException`                  | `Exists` was expected, stream does not exist           |
| `StreamAlreadyExistsException`             | `NoStream` was expected, stream already has records    |
| `StreamDeletedException`                   | Stream was soft-deleted                                |
| `StreamTombstonedException`                | Stream was permanently deleted                         |
| `StreamRevisionConflictException`          | Exact revision was expected, current revision differs  |
| `MaxAppendRecordSizeExceededException`     | A single record exceeds the server size limit          |
| `MaxAppendTransactionSizeExceededException`| The transaction exceeds the server size limit          |

### `AppendRecords` failures

`AppendStream` collapses a failed consistency check into one of the typed exceptions above. `AppendRecords` does not, because a single request can contain checks against several streams. When any check fails, it throws `AppendConsistencyViolationException` carrying every violation:

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

## Record builder options

`AppendRecord.New` returns a builder with the following methods:

| Method                              | Description                                                            |
|-------------------------------------|------------------------------------------------------------------------|
| `ForStream(StreamName)`             | Set the destination stream                                             |
| `WithId(Guid)`                      | Set a specific record id (auto-generated if omitted)                   |
| `WithValue(object)`                 | Set the payload                                                        |
| `WithDataFormat(SchemaDataFormat)`  | Override serialization format (`Json`, `Protobuf`, `Avro`, `Bytes`)    |
| `WithMetadata(Metadata)`            | Replace metadata wholesale                                             |
| `WithMetadata<T>(string, T)`        | Add or update a single metadata entry                                  |
| `WithTimestamp(DateTimeOffset)`     | Override the record timestamp                                          |

`Build()` validates the record and throws `ValidationException` if the value is missing. The destination stream is enforced when the record is added to a request, not on the record itself, so a record built without `ForStream` is valid as long as the request supplies one.
