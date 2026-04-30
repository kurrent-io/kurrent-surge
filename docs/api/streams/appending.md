---
order: 1
---

# Appending events

Events are the fundamental unit of data in KurrentDB. You append events to named streams, and each stream maintains an ordered sequence of events identified by a revision number.

## Append a single event

Build a record with the `AppendRecord.New` builder and pass it to `AppendStream`:

```csharp
using Kurrent.Client.Streams;

var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new {
        OrderId  = "order-123",
        Product  = "Widget",
        Quantity = 5
    })
    .Build();

var result = await client.Streams.AppendStream(record);

Console.WriteLine($"Stream:   {result.Stream}");
Console.WriteLine($"Revision: {result.Revision}");
Console.WriteLine($"Position: {result.Position}");
```

`AppendStream` serializes the value as JSON by default. If the stream does not exist, it is created automatically.

The result is an `AppendStreamResponse` with three properties: `Stream` (`StreamName`), `Revision` (`Revision`), and `Position` (`LogPosition`).

## Append multiple events

Use `AppendStreamRequest.For(stream)` to build a multi-record request. Records on a `For(stream)` request are stamped with the target stream automatically:

```csharp
var request = AppendStreamRequest.For("order-456")
    .WithMessage(new { OrderId = "order-456", Product = "Gadget" })
    .WithMessage(new { OrderId = "order-456", ConfirmedAt = DateTimeOffset.UtcNow })
    .WithMessage(new { OrderId = "order-456", TrackingId  = "TRK-789" })
    .Build();

var result = await client.Streams.AppendStream(request);
```

All records in the request are appended atomically. They all succeed or all fail together.

You can also pass pre-built records:

```csharp
var request = AppendStreamRequest.For("order-456")
    .WithRecord(AppendRecord.New.WithValue(new { OrderId = "order-456" }).Build())
    .WithRecord(AppendRecord.New.WithValue(new { ConfirmedAt = DateTimeOffset.UtcNow }).Build())
    .Build();
```

## Optimistic concurrency

`ExpectedStreamState` controls optimistic concurrency. It tells the server what state the stream should be in before the append is accepted.

### Any state (no concurrency check)

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.Any)
    .WithMessage(payload)
    .Build();
```

The append succeeds regardless of whether the stream exists or what revision it is at. This is the default behavior. The simple `AppendStream(record)` and `AppendStream(stream, records)` shorthands behave the same.

### Stream must not exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.NoStream)
    .WithMessage(new { OrderId = "order-123" })
    .Build();
```

The append fails with `StreamAlreadyExistsException` if the stream already has events. Use this for creation events where you need to guarantee uniqueness.

### Stream must exist

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(ExpectedStreamState.Exists)
    .WithMessage(payload)
    .Build();
```

The append fails with `StreamNotFoundException` if the stream does not exist.

### Exact revision match

```csharp
var request = AppendStreamRequest.For("order-123")
    .ExpectingState(Revision.From(4))
    .WithMessage(payload)
    .Build();
```

The append fails with `StreamRevisionConflictException` if the stream's current revision is not exactly 4. This is the strongest concurrency guarantee. Use it when multiple writers may contend on the same stream.

A typical pattern is to read the stream first, then append with the last known revision:

```csharp
var last = await client.Streams.ReadLastStreamRecord("order-123");

var request = AppendStreamRequest.For("order-123")
    .ExpectingState(last.Revision)
    .WithMessage(payload)
    .Build();

var result = await client.Streams.AppendStream(request);
```

`Revision` implicitly converts into `ExpectedStreamState`, so passing `last.Revision` directly works.

### Summary

| Value                            | Behavior                              |
|----------------------------------|---------------------------------------|
| `ExpectedStreamState.Any`        | No check, always succeeds             |
| `ExpectedStreamState.NoStream`   | Stream must not exist                 |
| `ExpectedStreamState.Exists`     | Stream must exist                     |
| `Revision.From(n)`               | Stream revision must match exactly    |

## Convenience overloads

`StreamsClient.AppendStream` provides several shorthand overloads. Pick whichever matches your shape:

```csharp
// Single pre-built record (stream baked into the record).
await client.Streams.AppendStream(record);

// Single record with concurrency check.
await client.Streams.AppendStream(record, ExpectedStreamState.NoStream);

// Multiple records to one stream.
await client.Streams.AppendStream("order-123", records);

// Multiple records with concurrency check.
await client.Streams.AppendStream("order-123", ExpectedStreamState.Any, records);

// Full request (most explicit form).
await client.Streams.AppendStream(request);
```

## Record builder options

`AppendRecord.New` returns a fluent builder:

| Method                              | Description                                                            |
|-------------------------------------|------------------------------------------------------------------------|
| `ForStream(StreamName)`             | Set the destination stream                                             |
| `WithId(Guid)`                      | Set a specific record id (auto-generated if omitted)                   |
| `WithValue(object)`                 | Set the payload                                                        |
| `WithDataFormat(SchemaDataFormat)`  | Override serialization format (`Json`, `Protobuf`, `Bytes`)            |
| `WithMetadata(Metadata)`            | Replace metadata wholesale                                             |
| `WithMetadata<T>(string, T)`        | Add or update a single metadata entry                                  |
| `WithTimestamp(DateTimeOffset)`     | Override the record timestamp                                          |

`Build()` validates the record and throws `ValidationException` if the value or stream is missing.

## Multi-stream appends

To append records to multiple streams in a single transaction, use `AppendRecords` with `AppendRecordsRequest.New()`:

```csharp
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

By default, payloads are serialized as JSON. Override per record:

```csharp
// JSON (default)
var jsonRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPayload)
    .Build();

// Raw bytes
var bytesRecord = AppendRecord.New
    .ForStream("blobs")
    .WithValue(rawBytes)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();

// Protobuf
var protoRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(protobufMessage)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();
```

## Error handling

`AppendStream` throws on failure. The exceptions are well-typed and let you pattern-match by catching specific types:

```csharp
try {
    await client.Streams.AppendStream(record, ExpectedStreamState.NoStream);
}
catch (StreamAlreadyExistsException) {
    // Stream already has events
}
catch (StreamRevisionConflictException ex) {
    // Expected revision did not match the stream's current state
    Console.WriteLine($"Expected {ex.ExpectedRevision}, was {ex.ActualRevision}");
}
catch (StreamTombstonedException) {
    // Stream was permanently deleted
}
```

### Common exceptions

| Exception                                  | Cause                                                       |
|--------------------------------------------|-------------------------------------------------------------|
| `StreamNotFoundException`                  | Stream does not exist (when `Exists` was expected)          |
| `StreamAlreadyExistsException`             | Stream exists (when `NoStream` was expected)                |
| `StreamTombstonedException`                | Stream was permanently deleted                              |
| `StreamRevisionConflictException`          | Current revision does not match expected                    |
| `MaxAppendRecordSizeExceededException`     | Single record exceeds server size limit                     |
| `MaxAppendTransactionSizeExceededException`| Total transaction exceeds server size limit                 |
