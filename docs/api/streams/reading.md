---
order: 2
---

# Reading events

Events can be read from individual streams or from the global log (`$all`). All read operations return async enumerables that stream results with backpressure support.

## Read from a stream

Read all events from a named stream:

```csharp
using Kurrent.Client.Streams;

var stream = await client.Streams.ReadStreamRecordsAsync("order-123");

await foreach (var item in stream) {
    item.Switch(
        record => Console.WriteLine($"{record.EventType} at revision {record.StreamRevision}"),
        heartbeat => { } // periodic server signal
    );
}
```

The returned `RecordStream` is an `IAsyncEnumerable` — it streams results lazily from the server. Each item is a `ReadRecordsResult`, which is either a `Record` or a `Heartbeat`.

### Start from a specific revision

```csharp
var stream = await client.Streams.ReadStreamRecordsAsync("order-123", start: 5);
```

This reads events starting at stream revision 5 (inclusive).

### First and last record

Read a single record from either end of a stream:

```csharp
var first = await client.Streams.ReadFirstStreamRecordAsync("order-123");
var last  = await client.Streams.ReadLastStreamRecordAsync("order-123");

Console.WriteLine($"First event: {first.EventType}");
Console.WriteLine($"Last event: {last.EventType} at revision {last.StreamRevision}");
```

## Read from the global log

The global log (`$all`) contains every event across all streams in commit order:

```csharp
var stream = await client.Streams.ReadRecordsAsync(LogPosition.Earliest);

await foreach (var item in stream) {
    item.Switch(
        record => Console.WriteLine($"[{record.Position}] {record.Stream}: {record.EventType}"),
        heartbeat => { }
    );
}
```

### Key positions

| Value | Description |
|-------|-------------|
| `LogPosition.Earliest` | Beginning of the log (position 0) |
| `LogPosition.Latest` | End of the log |

## Read options builder

For full control over read behavior, use the fluent builders.

### Stream reads

```csharp
var options = ReadStream.From("order-123")
    .FromStart()
    .Forward()
    .WithMaxRecords(100)
    .Build();

var stream = await client.Streams.ReadStreamRecordsAsync(options);
```

#### Builder methods

| Method | Description |
|--------|-------------|
| `FromStart()` | Start at the first revision, read forwards |
| `FromEnd()` | Start at the last revision, read backwards |
| `FromRevision(StreamRevision)` | Start at a specific revision |
| `Forward()` | Read in ascending order |
| `Backwards()` | Read in descending order |
| `WithMaxRecords(long)` | Limit the number of records returned |
| `WithBufferCapacity(int)` | Set the streaming buffer size (default: 1000) |
| `WithFilter(IRecordFilter)` | Apply a client-side filter |
| `WithFilterExpression(string)` | Apply a SQL-like filter expression |
| `SkipDecoding()` | Skip payload deserialization for metadata-only reads |

### Global log reads

```csharp
var options = ReadLog.FromStart()
    .WithMaxRecords(500)
    .WithFilter(RecordFilter.ByStreamPrefix("order-"))
    .Build();

var stream = await client.Streams.ReadRecordsAsync(options);
```

#### Entry points

| Method | Description |
|--------|-------------|
| `ReadLog.FromStart()` | Position 0, forwards |
| `ReadLog.FromEnd()` | Latest position, backwards |
| `ReadLog.From(LogPosition)` | Custom start position |

The builder methods are the same as for stream reads.

## Reading direction

Read forwards (oldest first) or backwards (newest first):

```csharp
// Last 10 events in a stream
var options = ReadStream.From("order-123")
    .FromEnd()
    .Backwards()
    .WithMaxRecords(10)
    .Build();

var stream = await client.Streams.ReadStreamRecordsAsync(options);
```

## Batch reading

Batch reading groups records into arrays, useful for processing in bulk:

```csharp
var batches = await client.Streams.ReadStreamBatchesAsync(
    stream: "order-123",
    batchSize: 50,
    batchTimeout: TimeSpan.FromMilliseconds(500)
);

await foreach (var item in batches) {
    item.Switch(
        records => {
            Console.WriteLine($"Batch of {records.Length} records");
            foreach (var record in records) {
                Console.WriteLine($"  {record.EventType}");
            }
        },
        heartbeat => { }
    );
}
```

The stream flushes a batch when either:
- The batch reaches `batchSize` records
- The `batchTimeout` elapses since the last flush
- A heartbeat arrives from the server

### Batch reading from the global log

```csharp
var batches = await client.Streams.ReadBatchesAsync(
    start: LogPosition.Earliest,
    batchSize: 100,
    batchTimeout: TimeSpan.FromSeconds(1)
);
```

### With options builder

```csharp
var options = ReadLog.FromStart()
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();

var batches = await client.Streams.ReadBatchesAsync(
    options,
    batchSize: 100,
    batchTimeout: TimeSpan.FromSeconds(1)
);
```

### Progress tracking

`RecordBatchStream` exposes progress counters:

```csharp
await foreach (var item in batches) {
    // process...
}

Console.WriteLine($"Total records: {batches.TotalRecordsRead}");
Console.WriteLine($"Total batches: {batches.TotalBatchesRead}");
```

## Filtering

Filters reduce the set of records returned from a read. They are applied client-side after the server response.

### By stream name

```csharp
// Exact match — one or more streams
RecordFilter.ByStream("order-123", "order-456")

// Prefix match
RecordFilter.ByStreamPrefix("order-")

// Category match (stream names follow the convention "category-id")
RecordFilter.ByCategory("order")

// Regex pattern
RecordFilter.ByStreamPattern(@"order-\d+")
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
RecordFilter.ByExpression("stream = 'order-123' AND schema = 'OrderPlaced'")
```

### Applying filters

Filters work with both stream reads and global log reads:

```csharp
var options = ReadLog.FromStart()
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();

var stream = await client.Streams.ReadRecordsAsync(options);
```

Or via the shorthand:

```csharp
var options = ReadLog.FromStart()
    .WithFilterExpression("category = 'order'")
    .Build();
```

## Inspect a single record

Read a single record at a known global log position:

```csharp
var record = await client.Streams.InspectRecordAsync(LogPosition.From(42));

Console.WriteLine($"Event: {record.EventType}");
Console.WriteLine($"Stream: {record.Stream}");
Console.WriteLine($"Data: {record.Data}");
```

By default, `InspectRecordAsync` skips payload deserialization (`skipDecoding: true`). To decode the payload:

```csharp
var record = await client.Streams.InspectRecordAsync(
    LogPosition.From(42),
    skipDecoding: false
);

Console.WriteLine($"Decoded value: {record.Value}");
```

## Working with records

The `Record` type contains the full event data:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Unique event identifier |
| `EventType` | `string` | Event type name (from schema) |
| `Stream` | `StreamName` | Source stream name |
| `StreamRevision` | `StreamRevision` | Position within the stream |
| `Position` | `RecordPosition` | Full position (log + stream) |
| `Timestamp` | `DateTime` | When the event was written |
| `Data` | `ReadOnlyMemory<byte>` | Raw event payload |
| `Value` | `object?` | Deserialized payload (if decoded) |
| `Metadata` | `Metadata` | Event metadata |
| `Schema` | `RecordSchemaInfo` | Schema information |
| `IsRedacted` | `bool` | Whether the event data was redacted |
| `IsDecoded` | `bool` | Whether `Value` has been deserialized |

### Handling heartbeats

Read results interleave records with heartbeats. Heartbeats are server signals indicating checkpoint progress or that the reader has caught up to the end of the stream.

```csharp
await foreach (var item in stream) {
    item.Switch(
        record => ProcessEvent(record),
        heartbeat => {
            if (heartbeat.IsCaughtUp)
                Console.WriteLine("Caught up to end of stream");
            if (heartbeat.IsCheckpoint)
                Console.WriteLine($"Checkpoint at position {heartbeat.Position}");
        }
    );
}
```

## Error handling

The idiomatic `Async` methods throw on failure. For explicit control, use the Result-based API:

```csharp
var result = await client.Streams.ReadRecords(
    ReadStream.From("order-123").FromStart().Build()
);

result.Switch(
    stream => { /* enumerate stream */ },
    error => error.Switch(
        streamNotFound  => Console.WriteLine("Stream does not exist"),
        streamDeleted   => Console.WriteLine("Stream was soft-deleted"),
        streamTombstoned => Console.WriteLine("Stream was permanently deleted"),
        positionNotFound => Console.WriteLine("Log position not found"),
        exception        => Console.WriteLine($"Unexpected: {exception.Message}")
    )
);
```

## Disposal

`RecordStream` and `RecordBatchStream` implement `IAsyncDisposable`. Use `await using` or dispose explicitly when breaking out of enumeration early:

```csharp
await using var stream = await client.Streams.ReadStreamRecordsAsync("order-123");

await foreach (var item in stream) {
    // breaking early is safe — disposal cleans up the server connection
    if (ShouldStop(item)) break;
}
```

When enumerating to completion with `await foreach`, disposal happens automatically.
