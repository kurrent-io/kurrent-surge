---
order: 2
---

# Reading records

<CopyMarkdownButton />

Records can be read from individual streams or from the global log (`$all`). All read operations return async enumerables that stream results lazily from the server.

## Read from a stream

Build a request with `ReadStreamRequest.New` and pass it to `ReadStream`:

```csharp
var request = ReadStreamRequest.New
    .ForStream("order-123")
    .Build();

var stream = client.Streams.ReadStream(request);

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine("{0} at revision {1}", record.TypeName, record.Revision),
        heartbeat => { } // periodic server signal
    );
}
```

`ReadStream` returns an `IAsyncEnumerable<ReadRecordsResult>`. Each `ReadRecordsResult` is a discriminated union of `Record` or `Heartbeat`. Use `Switch` (or pattern matching) to handle both cases.

::: tip
For a simple read from the start of a stream, the `ReadStreamRecords` extension skips the builder:

```csharp
var stream = client.Streams.ReadStreamRecords("order-123");
```
:::

### Start from a specific revision

```csharp
var request = ReadStreamRequest.New
    .ForStream("order-123")
    .FromRevision(Revision.From(5))
    .Build();

var stream = client.Streams.ReadStream(request);
```

This reads records starting at stream revision 5 (inclusive).

::: tip
The `ReadStreamRecords` extension takes a revision directly:

```csharp
var stream = client.Streams.ReadStreamRecords("order-123", Revision.From(5));
```
:::

### First and last record

`ReadStreamEdge` reads a single record at the start or end of a stream:

```csharp
var first = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Forwards,  skipDecoding: false, cancellationToken: default);
var last  = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Backwards, skipDecoding: false, cancellationToken: default);

Console.WriteLine("First record: {0}", first.TypeName);
Console.WriteLine("Last record:  {0} at revision {1}", last.TypeName, last.Revision);
```

::: tip
`ReadFirstStreamRecord` and `ReadLastStreamRecord` are shorthand extensions for the common case:

```csharp
var first = await client.Streams.ReadFirstStreamRecord("order-123");
var last  = await client.Streams.ReadLastStreamRecord("order-123");
```
:::

## Read from the global log

The global log (`$all`) contains every record across all streams in commit order. Build a request with `ReadRecordsRequest.New` and pass it to `ReadRecords`:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .Build();

var stream = client.Streams.ReadRecords(request);

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine("[{0}] {1}: {2}", record.Position, record.Stream, record.TypeName),
        heartbeat => { }
    );
}
```

::: tip
For a position-only read, the `ReadRecords` extension takes a `LogPosition` directly:

```csharp
var stream = client.Streams.ReadRecords(LogPosition.Earliest);
```
:::

### Key positions

| Value                    | Description                       |
|--------------------------|-----------------------------------|
| `LogPosition.Earliest`   | Beginning of the log (position 0) |
| `LogPosition.Latest`     | End of the log                    |
| `LogPosition.From(long)` | A specific position               |

## Builder methods

`ReadStreamRequest.New` (per-stream) and `ReadRecordsRequest.New` (global log) share most of their fluent surface.

| Method                         | Applies to     | Description                                          |
|--------------------------------|----------------|------------------------------------------------------|
| `ForStream(StreamName)`        | Stream         | Set the stream to read from                          |
| `FromStart()`                  | Stream         | Start at the first revision, read forwards           |
| `FromEnd()`                    | Stream         | Start at the last revision, read backwards           |
| `FromRevision(Revision)`       | Stream         | Start at a specific revision                         |
| `FromPosition(LogPosition)`    | Global log     | Start at a specific log position                     |
| `Forward()`                    | Both           | Read in ascending order                              |
| `Backwards()`                  | Both           | Read in descending order                             |
| `WithMaxRecords(long)`         | Both           | Limit the number of records returned                 |
| `WithFilter(IRecordFilter)`    | Both           | Apply a filter (matched client-side)                 |
| `WithFilterExpression(string)` | Both           | Apply a SQL-like filter expression                   |
| `SkipDecoding()`               | Both           | Skip payload deserialization for metadata-only reads |

## Reading direction

Read forwards (oldest first) or backwards (newest first):

```csharp
// Last 10 records in a stream
var request = ReadStreamRequest.New
    .ForStream("order-123")
    .FromEnd()
    .Backwards()
    .WithMaxRecords(10)
    .Build();

var stream = client.Streams.ReadStream(request);
```

## Filtering

Filters reduce the set of records returned from a read. They work with both stream reads and global log reads.

::: tabs#filter-types

@tab By stream name

```csharp
// Exact match (single stream)
RecordFilter.ByStream("order-123")

// Category match (stream names follow the convention "category-id")
RecordFilter.ByCategory("order")

// Regex pattern. Not for index streams (use ByCategory/BySchema/ByUserIndex instead)
RecordFilter.ByStreamPattern(@"^order-\d+$")
```

@tab By record type

```csharp
// Exact match (single schema)
RecordFilter.BySchema("OrderPlaced")
```

For multiple schemas or pattern matches, use a SQL-like expression:

```csharp
RecordFilter.ByExpression("schema_name IN ('OrderPlaced', 'OrderConfirmed')")
RecordFilter.ByExpression("schema_name LIKE 'Order%'")
```

@tab SQL-like expressions

```csharp
RecordFilter.ByExpression("stream = 'order-123' AND schema_name = 'OrderPlaced'")
```

:::

Apply a filter with `WithFilter` or pass an expression directly with `WithFilterExpression`:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();

var withExpression = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilterExpression("stream like 'order-%'")
    .Build();
```

## Inspect a single record

`InspectRecord` reads a single record at a known global log position. The `skipDecoding` argument controls whether the payload is deserialized:

```csharp
// Metadata only, no payload decode
var record = await client.Streams.InspectRecord(LogPosition.From(42), skipDecoding: true);

Console.WriteLine("Type:   {0}", record.TypeName);
Console.WriteLine("Stream: {0}", record.Stream);
Console.WriteLine("Data:   {0} bytes", record.Data.Length);

// Decode the payload
var decoded = await client.Streams.InspectRecord(LogPosition.From(42), skipDecoding: false);

Console.WriteLine("Decoded value: {0}", decoded.Value);
```

::: tip
The single-argument overload defaults to `skipDecoding: true`:

```csharp
var record = await client.Streams.InspectRecord(LogPosition.From(42));
```
:::

## Working with records

The `Record` type contains the full record data:

| Property         | Type                  | Description                                                      |
|------------------|-----------------------|------------------------------------------------------------------|
| `Id`             | `Guid`                | Unique record identifier                                         |
| `TypeName`       | `string`              | CLR type name when decoded, otherwise the schema name            |
| `Stream`         | `StreamName`          | Source stream name. Equivalent to `Position.Stream`.             |
| `Revision`       | `Revision`            | Position within the stream. Equivalent to `Position.Revision`.   |
| `Position`       | `RecordPosition`      | Full position (log position + stream + partition)                |
| `Timestamp`      | `DateTime`            | When the record was written                                      |
| `Data`           | `ReadOnlyMemory<byte>`| Raw record payload                                               |
| `Value`          | `object?`             | Deserialized payload (if decoded)                                |
| `ValueType`      | `Type`                | CLR type of the deserialized value                               |
| `Metadata`       | `Metadata`            | Record metadata                                                  |
| `Schema`         | `RecordSchemaInfo`    | Schema name and format                                           |
| `Category`       | `string`              | Category portion of the stream name                              |
| `IsRedacted`     | `bool`                | Whether the record data was redacted                             |
| `IsDecoded`      | `bool`                | Whether `Value` has been deserialized                            |

Use `record.As<T>()` to cast the decoded value to a specific type with no boxing for reference types.

### Handling heartbeats

Read results interleave records with heartbeats. Heartbeats are server signals indicating checkpoint progress or that the reader has caught up to the end of the stream.

```csharp
await foreach (var item in stream) {
    item.Switch(
        record    => ProcessRecord(record),
        heartbeat => {
            if (heartbeat.IsCaughtUp)
                Console.WriteLine("Caught up to end of stream");
            if (heartbeat.IsCheckpoint)
                Console.WriteLine("Checkpoint at position {0}", heartbeat.Position);
        }
    );
}
```

## Materializing results

To collect all records into a list, project the stream with LINQ. Filter out heartbeats first:

```csharp
var request = ReadStreamRequest.New.ForStream("order-123").Build();

var records = await client.Streams.ReadStream(request)
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord)
    .ToListAsync();
```

::: tip
The `ToListAsync` extension on a read result drops heartbeats automatically:

```csharp
List<Record> records = await client.Streams.ReadStream(request).ToListAsync();
```
:::

## Error handling

Read operations throw on failure. Catch the well-typed exceptions when you need to handle specific cases:

```csharp
try {
    var request = ReadStreamRequest.New.ForStream("order-123").Build();

    await foreach (var item in client.Streams.ReadStream(request)) {
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
