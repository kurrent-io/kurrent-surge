---
order: 1
---

# Appending events

Events are the fundamental unit of data in KurrentDB. You append events to named streams, and each stream maintains an ordered sequence of events identified by a revision number.

## Append a single event

The simplest way to append an event:

```csharp
using Kurrent.Client.Streams;

var record = new AppendRecord("OrderPlaced", new {
    OrderId = "order-123",
    Product = "Widget",
    Quantity = 5
});

var result = await client.Streams.AppendAsync("order-123", record);

Console.WriteLine($"Stream: {result.Stream}");
Console.WriteLine($"Position: {result.Position}");
Console.WriteLine($"Revision: {result.StreamRevision}");
```

The first argument is the event type name. The second is the payload — any serializable object. The client serializes it as JSON by default.

If the stream does not exist, it is created automatically.

## Append multiple events

Use `AppendStreamRequest` to append multiple events in a single operation:

```csharp
var request = new AppendStreamRequest(
    stream: "order-456",
    expectedState: ExpectedStreamState.Any,
    messages: [
        new AppendRecord("OrderPlaced", new { OrderId = "order-456", Product = "Gadget" }),
        new AppendRecord("OrderConfirmed", new { OrderId = "order-456" }),
        new AppendRecord("OrderShipped", new { OrderId = "order-456", TrackingId = "TRK-789" })
    ]
);

var result = await client.Streams.AppendAsync(request);
```

All events in the request are appended atomically — they all succeed or all fail.

## Optimistic concurrency

`ExpectedStreamState` controls optimistic concurrency. It tells the server what state the stream should be in before the append is accepted.

### Any state (no concurrency check)

```csharp
var request = new AppendStreamRequest(
    stream: "order-123",
    expectedState: ExpectedStreamState.Any,
    messages: [new AppendRecord("OrderPlaced", payload)]
);
```

The append succeeds regardless of whether the stream exists or what revision it is at. This is the default when using the shorthand `AppendAsync(stream, record)`.

### Stream must not exist

```csharp
var request = new AppendStreamRequest(
    stream: "order-123",
    expectedState: ExpectedStreamCondition.NoStream,
    messages: [new AppendRecord("OrderCreated", payload)]
);
```

The append fails with `StreamAlreadyExistsException` if the stream already has events. Use this for creation events where you need to guarantee uniqueness.

### Stream must exist

```csharp
var request = new AppendStreamRequest(
    stream: "order-123",
    expectedState: ExpectedStreamCondition.Exists,
    messages: [new AppendRecord("OrderUpdated", payload)]
);
```

The append fails with `StreamNotFoundException` if the stream does not exist.

### Exact revision match

```csharp
var request = new AppendStreamRequest(
    stream: "order-123",
    expectedState: StreamRevision.From(4),
    messages: [new AppendRecord("OrderUpdated", payload)]
);
```

The append fails with `StreamRevisionConflictException` if the stream's current revision is not exactly 4. This is the strongest concurrency guarantee — use it when multiple writers may contend on the same stream.

A typical pattern is to read the stream first, then append with the last known revision:

```csharp
var last = await client.Streams.ReadLastStreamRecordAsync("order-123");

var request = new AppendStreamRequest(
    stream: "order-123",
    expectedState: last.StreamRevision,
    messages: [new AppendRecord("OrderUpdated", payload)]
);

var result = await client.Streams.AppendAsync(request);
```

### Summary

| Value | Behavior |
|-------|----------|
| `ExpectedStreamState.Any` | No check - always succeeds |
| `ExpectedStreamCondition.NoStream` | Stream must not exist |
| `ExpectedStreamCondition.Exists` | Stream must exist |
| `StreamRevision.From(revision)` | Stream revision must match exactly |

## Builder API

For more complex scenarios, use the fluent builder:

```csharp
var request = AppendStreamRequest.New()
    .ForStream("order-789")
    .ExpectingRevision(ExpectedStreamCondition.NoStream)
    .WithRecord(new AppendRecord("OrderPlaced", new { OrderId = "order-789" }))
    .WithRecord(new AppendRecord("OrderConfirmed", new { OrderId = "order-789" }))
    .Build();

var result = await client.Streams.AppendAsync(request);
```

Build individual records with `AppendRecord.New`:

```csharp
var record = AppendRecord.New
    .WithRecordId(Guid.NewGuid())
    .WithValue(new { OrderId = "order-789", Total = 99.95m })
    .WithMetadata("correlation-id", correlationId)
    .Build();
```

### Record builder options

| Method | Description |
|--------|-------------|
| `WithRecordId(Guid)` | Set a specific event ID (auto-generated if omitted) |
| `WithValue(object)` | Set the event payload |
| `WithDataFormat(SchemaDataFormat)` | Override serialization format (`Json`, `Protobuf`, `Avro`, `Bytes`) |
| `WithMetadata(...)` | Attach metadata as key-value pairs, dictionary, or `Metadata` instance |

## Append sessions

Append sessions allow you to append to multiple streams within a single server round-trip:

```csharp
var requests = new[] {
    new AppendStreamRequest("orders", ExpectedStreamState.Any, [
        new AppendRecord("OrderPlaced", orderPayload)
    ]),
    new AppendStreamRequest("inventory", ExpectedStreamState.Any, [
        new AppendRecord("InventoryReserved", inventoryPayload)
    ])
};

var result = await client.Streams.AppendSessionAsync(requests);

foreach (var success in result) {
    Console.WriteLine($"Stream: {success.Stream}, Revision: {success.StreamRevision}");
}
```

The `AppendSessionSuccess` result implements `IReadOnlyList<AppendStreamSuccess>` — iterate it to inspect per-stream results.

## Data formats

By default, event payloads are serialized as JSON. Override this per-record:

```csharp
// JSON (default)
var jsonRecord = new AppendRecord("OrderPlaced", orderPayload);

// Raw bytes
var bytesRecord = AppendRecord.New
    .WithValue(rawBytes)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();

// Protobuf
var protoRecord = AppendRecord.New
    .WithValue(protobufMessage)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();
```

## Error handling

The idiomatic `AppendAsync` methods throw on failure. For explicit error handling without exceptions, use the Result-based API:

```csharp
var result = await client.Streams.Append(
    new AppendStreamRequest("order-123", ExpectedStreamCondition.NoStream, [record])
);

result.Switch(
    success => Console.WriteLine($"Appended at revision {success.StreamRevision}"),
    error => error.Switch(
        streamNotFound      => Console.WriteLine("Stream not found"),
        streamAlreadyExists => Console.WriteLine("Stream already exists"),
        streamTombstoned    => Console.WriteLine("Stream was tombstoned"),
        revisionConflict    => Console.WriteLine($"Expected revision mismatch"),
        alreadyInSession    => Console.WriteLine("Stream already in append session"),
        maxRecordSize       => Console.WriteLine("Record exceeds max size"),
        maxTransactionSize  => Console.WriteLine("Transaction exceeds max size"),
        noRequests          => Console.WriteLine("No requests in session"),
        exception           => Console.WriteLine($"Unexpected: {exception.Message}")
    )
);
```

### Error types

| Error | Cause |
|-------|-------|
| `StreamNotFoundException` | Stream does not exist (when `Exists` expected) |
| `StreamAlreadyExistsException` | Stream exists (when `NoStream` expected) |
| `StreamTombstonedException` | Stream was permanently deleted |
| `StreamRevisionConflictException` | Current revision does not match expected |
| `MaxAppendRecordSizeExceededException` | Single record exceeds server size limit |
| `MaxAppendTransactionSizeExceededException` | Total transaction exceeds server size limit |
