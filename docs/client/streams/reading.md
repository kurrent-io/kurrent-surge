---
order: 2
---

# Read

<CopyMarkdownButton />

Records can be read from a single stream with `ReadStream` or from the global log with `ReadRecords`. Both return an async sequence of read results that the server streams lazily.

## Read from a stream

Build a request with `ReadStreamRequest.New()` and pass it to `ReadStream`:

```csharp
var request = ReadStreamRequest.New()
    .Stream("order-123")
    .Build();

var stream = client.Streams.ReadStream(request);

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine("{0} at revision {1}", record.TypeName, record.Revision),
        heartbeat => { } // periodic server signal
    );
}
```

Each result is a `Record` or a `Heartbeat`. Use `Switch` (or pattern matching) to handle both cases. See [Handle heartbeats](#handle-heartbeats) for what heartbeats carry.

::: tip
For a simple read from the start of a stream, the `ReadStreamRecords` extension skips the builder:

```csharp
var stream = client.Streams.ReadStreamRecords("order-123");
```
:::

To start partway through the stream, set the revision:

```csharp
var request = ReadStreamRequest.New()
    .Stream("order-123")
    .FromRevision(Revision.From(5))
    .Build();
```

This reads forwards from revision 5 (inclusive).

To read newest first, combine `FromEnd()` with `Backwards()` and an upper bound:

```csharp
var request = ReadStreamRequest.New()
    .Stream("order-123")
    .FromEnd()
    .Backwards()
    .Limit(10)
    .Build();
```

This returns the last 10 records, newest first.

## Read the first or last record

`ReadStreamEdge` reads a single record at the start or end of a stream:

```csharp
var first = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Forwards,  false, default);
var last  = await client.Streams.ReadStreamEdge("order-123", ReadDirection.Backwards, false, default);

Console.WriteLine("First record: {0}", first.TypeName);
Console.WriteLine("Last record:  {0} at revision {1}", last.TypeName, last.Revision);
```

The third argument is `skipDecoding`. Set it to `true` to skip payload deserialization when you only need metadata.

::: tip
`ReadFirstStreamRecord` and `ReadLastStreamRecord` are shorthand extensions for the common case:

```csharp
var first = await client.Streams.ReadFirstStreamRecord("order-123");
var last  = await client.Streams.ReadLastStreamRecord("order-123");
```
:::

## Read from the global log

The global log contains every record across all streams in commit order. Build a request with `ReadRecordsRequest.New()` and pass it to `ReadRecords`:

```csharp
var request = ReadRecordsRequest.New()
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

`LogPosition` exposes three values for `FromPosition`:

| Value          | Description                       |
|----------------|-----------------------------------|
| `Earliest`     | Beginning of the log (position 0) |
| `Latest`       | End of the log                    |
| `From(long)`   | A specific position               |

::: tip
For a position-only read, the `ReadRecords` extension takes a `LogPosition` directly:

```csharp
var stream = client.Streams.ReadRecords(LogPosition.Earliest);
```
:::

## Inspect a single record

`InspectRecord` reads one record at a known log position. The `skipDecoding` flag controls whether the payload is deserialized:

```csharp
// Metadata only, no payload decode
var record = await client.Streams.InspectRecord(LogPosition.From(42), true);

Console.WriteLine("Type:   {0}", record.TypeName);
Console.WriteLine("Stream: {0}", record.Stream);
Console.WriteLine("Data:   {0} bytes", record.Data.Length);

// Decode the payload
var decoded = await client.Streams.InspectRecord(LogPosition.From(42), false);

Console.WriteLine("Decoded value: {0}", decoded.Value);
```

::: tip
The single-argument overload defaults `skipDecoding` to `true`:

```csharp
var record = await client.Streams.InspectRecord(LogPosition.From(42));
```
:::

## Filter results

Filters narrow what a read returns. Apply one with `Filter`:

```csharp
var request = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter(RecordFilter.ByCategory("order"))
    .Build();
```

Or pass a SQL-like expression directly:

```csharp
var request = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter("stream LIKE 'order-%'")
    .Build();
```

See [Filters](./filters.md) for the full set of factories, the expression language, and which filters are allowed on stream reads.

## Handle heartbeats

Read results interleave records with heartbeats. Each heartbeat carries either a checkpoint position or a signal that the reader has caught up to the end of available data.

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

For one-shot reads that just need records, see [Materialize a list](#materialize-a-list).

## Materialize a list

To collect every record into a list, project the stream with LINQ. Drop heartbeats first:

```csharp
var request = ReadStreamRequest.New().Stream("order-123").Build();

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

## Handle read errors

Read operations throw on failure. Catch the typed exceptions when you need to handle specific cases:

```csharp
try {
    var request = ReadStreamRequest.New().Stream("order-123").Build();

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

See [Error handling](../error-handling.md) for the full throwing model.

## Reference

### Builder methods

`ReadStreamRequest.New()` (per-stream) and `ReadRecordsRequest.New()` (global log) share most of their fluent surface.

| Method                       | Applies to | Description                                          |
|------------------------------|------------|------------------------------------------------------|
| `Stream(StreamName)`         | Stream     | Set the stream to read from                          |
| `FromStart()`                | Stream     | Start at the first revision, read forwards           |
| `FromEnd()`                  | Stream     | Start at the last revision, read backwards           |
| `FromRevision(Revision)`     | Stream     | Start at a specific revision                         |
| `FromPosition(LogPosition)`  | Global log | Start at a specific log position                     |
| `Forwards()`                 | Both       | Read in ascending order                              |
| `Backwards()`                | Both       | Read in descending order                             |
| `Limit(long)`                | Both       | Limit the number of records returned                 |
| `Filter(IRecordFilter)`      | Both       | Apply a filter                                       |
| `Filter(string)`             | Both       | Apply a SQL-like filter expression                   |
| `SkipDecoding(bool)`         | Both       | Skip payload deserialization for metadata-only reads |

### Record properties

| Property     | Type                   | Description                                                   |
|--------------|------------------------|---------------------------------------------------------------|
| `Id`         | `Guid`                 | Unique record identifier                                      |
| `TypeName`   | `string`               | CLR type name when decoded, otherwise the schema name         |
| `Stream`     | `StreamName`           | Source stream name. Equivalent to `Position.Stream`           |
| `Revision`   | `Revision`             | Position within the stream. Equivalent to `Position.Revision` |
| `Position`   | `RecordPosition`       | Full position (log position + stream + partition)             |
| `Timestamp`  | `DateTime`             | When the record was written                                   |
| `Data`       | `ReadOnlyMemory<byte>` | Raw record payload                                            |
| `Value`      | `object?`              | Deserialized payload (if decoded)                             |
| `ValueType`  | `Type`                 | CLR type of the deserialized value                            |
| `Metadata`   | `Metadata`             | Record metadata                                               |
| `Schema`     | `RecordSchemaInfo`     | Schema name and format                                        |
| `Category`   | `string`               | Category portion of the stream name                           |
| `IsRedacted` | `bool`                 | Whether the record data was redacted                          |
| `IsDecoded`  | `bool`                 | Whether `Value` has been deserialized                         |

Use `record.As<T>()` to cast the decoded value to a specific type with no boxing for reference types.

### Common exceptions

| Exception                      | Cause                                                   |
|--------------------------------|---------------------------------------------------------|
| `StreamNotFoundException`      | Stream does not exist                                   |
| `StreamDeletedException`       | Stream was soft-deleted                                 |
| `StreamTombstonedException`    | Stream was permanently deleted                          |
| `LogPositionNotFoundException` | Global log position does not exist                      |
| `OperationCanceledException`   | The cancellation token was cancelled during enumeration |
