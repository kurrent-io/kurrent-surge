---
order: 2
---

# Reading events

Events can be read from individual streams or from the global log (`$all`). All read operations return async enumerables that stream results lazily from the server.

## Read from a stream

Read all events from a named stream:

```csharp
using Kurrent.Client.Streams;

var stream = client.Streams.ReadStreamRecords("order-123");

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine($"{record.TypeName} at revision {record.Revision}"),
        heartbeat => { } // periodic server signal
    );
}
```

`ReadStreamRecords` returns an `IAsyncEnumerable<ReadRecordsResult>`. Each `ReadRecordsResult` is a discriminated union of `Record` or `Heartbeat`. Use `Switch` (or pattern matching) to handle both cases.

### Start from a specific revision

```csharp
var stream = client.Streams.ReadStreamRecords("order-123", Revision.From(5));
```

This reads events starting at stream revision 5 (inclusive).

### First and last record

```csharp
var first = await client.Streams.ReadFirstStreamRecord("order-123");
var last  = await client.Streams.ReadLastStreamRecord("order-123");

Console.WriteLine($"First event: {first.TypeName}");
Console.WriteLine($"Last event:  {last.TypeName} at revision {last.Revision}");
```

## Read from the global log

The global log (`$all`) contains every event across all streams in commit order:

```csharp
var stream = client.Streams.ReadRecords(LogPosition.Earliest);

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine($"[{record.Position}] {record.Stream}: {record.TypeName}"),
        heartbeat => { }
    );
}
```

### Key positions

| Value                    | Description                       |
|--------------------------|-----------------------------------|
| `LogPosition.Earliest`   | Beginning of the log (position 0) |
| `LogPosition.Latest`     | End of the log                    |
| `LogPosition.From(long)` | A specific position               |

## Read request builders

For full control over read behavior, build a request with `ReadStreamRequest.New` (per-stream) or `ReadRecordsRequest.New` (global log) and pass it to `ReadStream`/`ReadRecords`.

### Stream reads

```csharp
var request = ReadStreamRequest.New
    .ForStream("order-123")
    .FromStart()
    .Forward()
    .WithMaxRecords(100)
    .Build();

var stream = client.Streams.ReadStream(request);
```

#### Builder methods

| Method                              | Description                                      |
|-------------------------------------|--------------------------------------------------|
| `ForStream(StreamName)`             | Set the stream to read from                      |
| `FromStart()`                       | Start at the first revision, read forwards       |
| `FromEnd()`                         | Start at the last revision, read backwards       |
| `FromRevision(Revision)`            | Start at a specific revision                     |
| `Forward()`                         | Read in ascending order                          |
| `Backwards()`                       | Read in descending order                         |
| `WithMaxRecords(long)`              | Limit the number of records returned             |
| `WithFilter(IRecordFilter)`         | Apply a filter (matched client-side)             |
| `WithFilterExpression(string)`      | Apply a SQL-like filter expression               |
| `SkipDecoding()`                    | Skip payload deserialization for metadata-only reads |

### Global log reads

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .Forward()
    .WithMaxRecords(500)
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();

var stream = client.Streams.ReadRecords(request);
```

`ReadRecordsRequest` exposes the same `Forward()`, `Backwards()`, `WithMaxRecords`, `WithFilter`, `WithFilterExpression`, and `SkipDecoding` methods as `ReadStreamRequest`. Set the start with `FromPosition(LogPosition)`.

## Reading direction

Read forwards (oldest first) or backwards (newest first):

```csharp
// Last 10 events in a stream
var request = ReadStreamRequest.New
    .ForStream("order-123")
    .FromEnd()
    .Backwards()
    .WithMaxRecords(10)
    .Build();

var stream = client.Streams.ReadStream(request);
```

## Filtering

Filters reduce the set of records returned from a read.

### By stream name

```csharp
// Exact match (single stream)
RecordFilter.ByStream("order-123")

// Category match (stream names follow the convention "category-id")
RecordFilter.ByCategory("order")

// Regex pattern. Not for index streams (use ByCategory/BySchema/ByUserIndex instead)
RecordFilter.ByStreamPattern(@"^order-\d+$")
```

### By event type (schema)

```csharp
// Exact match
RecordFilter.BySchema("OrderPlaced", "OrderConfirmed")

// Prefix match
RecordFilter.BySchemaPrefix("Order")

// Regex pattern
RecordFilter.BySchemaPattern(@"Order(Placed|Confirmed)")
```

### SQL-like expressions

```csharp
RecordFilter.ByExpression("stream = 'order-123' AND schema_name = 'OrderPlaced'")
```

### Applying filters

Filters work with both stream reads and global log reads:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();

var stream = client.Streams.ReadRecords(request);
```

Or with a SQL-like expression:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilterExpression("stream like 'order-%'")
    .Build();
```

## Inspect a single record

Read a single record at a known global log position:

```csharp
var record = await client.Streams.InspectRecord(LogPosition.From(42));

Console.WriteLine($"Type:   {record.TypeName}");
Console.WriteLine($"Stream: {record.Stream}");
Console.WriteLine($"Data:   {record.Data.Length} bytes");
```

By default, `InspectRecord` skips payload deserialization (`skipDecoding: true`). To decode the payload:

```csharp
var record = await client.Streams.InspectRecord(LogPosition.From(42), skipDecoding: false);

Console.WriteLine($"Decoded value: {record.Value}");
```

## Working with records

The `Record` type contains the full event data:

| Property         | Type                  | Description                                                      |
|------------------|-----------------------|------------------------------------------------------------------|
| `Id`             | `Guid`                | Unique record identifier                                         |
| `TypeName`       | `string`              | CLR type name when decoded, otherwise the schema name            |
| `Stream`         | `StreamName`          | Source stream name. Equivalent to `Position.Stream`.             |
| `Revision`       | `Revision`            | Position within the stream. Equivalent to `Position.Revision`.   |
| `Position`       | `RecordPosition`      | Full position (log position + stream + partition)                |
| `Timestamp`      | `DateTime`            | When the event was written                                       |
| `Data`           | `ReadOnlyMemory<byte>`| Raw event payload                                                |
| `Value`          | `object?`             | Deserialized payload (if decoded)                                |
| `ValueType`      | `Type`                | CLR type of the deserialized value                               |
| `Metadata`       | `Metadata`            | Event metadata                                                   |
| `Schema`         | `RecordSchemaInfo`    | Schema name and format                                           |
| `Category`       | `string`              | Category portion of the stream name                              |
| `IsRedacted`     | `bool`                | Whether the event data was redacted                              |
| `IsDecoded`      | `bool`                | Whether `Value` has been deserialized                            |

Use `record.As<T>()` to cast the decoded value to a specific type with no boxing for reference types.

### Handling heartbeats

Read results interleave records with heartbeats. Heartbeats are server signals indicating checkpoint progress or that the reader has caught up to the end of the stream.

```csharp
await foreach (var item in stream) {
    item.Switch(
        record    => ProcessEvent(record),
        heartbeat => {
            if (heartbeat.IsCaughtUp)
                Console.WriteLine("Caught up to end of stream");
            if (heartbeat.IsCheckpoint)
                Console.WriteLine($"Checkpoint at position {heartbeat.Position}");
        }
    );
}
```

## Materializing results

If you only care about records (and want to drop heartbeats), use the `ToListAsync()` extension:

```csharp
List<Record> records = await client.Streams.ReadStreamRecords("order-123").ToListAsync();
```

For finer control, project the stream with LINQ:

```csharp
var records = await client.Streams.ReadStreamRecords("order-123")
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord)
    .ToListAsync();
```

## Error handling

Read operations throw on failure. Catch the well-typed exceptions when you need to handle specific cases:

```csharp
try {
    await foreach (var item in client.Streams.ReadStreamRecords("order-123")) {
        // ...
    }
}
catch (StreamNotFoundException) {
    // Stream does not exist
}
catch (StreamDeletedException) {
    // Stream was soft-deleted
}
catch (StreamTombstonedException) {
    // Stream was permanently deleted
}
catch (LogPositionNotFoundException) {
    // Log position does not exist
}
```

### Common exceptions

| Exception                         | Cause                                                       |
|-----------------------------------|-------------------------------------------------------------|
| `StreamNotFoundException`         | Stream does not exist.                                      |
| `StreamDeletedException`          | Stream was soft-deleted.                                    |
| `StreamTombstonedException`       | Stream was permanently deleted.                             |
| `LogPositionNotFoundException`    | Global log position does not exist.                         |
| `OperationCanceledException`      | The cancellation token was cancelled during enumeration.    |
