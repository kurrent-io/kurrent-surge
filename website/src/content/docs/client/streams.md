---
title: Streams
description: Append, read, subscribe and manage streams.
---

The largest module — 14 operations across four groups.

## Append

```csharp
ValueTask<AppendStreamResponse>  AppendStream(StreamName, IAsyncEnumerable<AppendRecord>, ExpectedStreamState, CancellationToken)
ValueTask<AppendRecordsResponse> AppendRecords(IAsyncEnumerable<AppendRecord>, AppendRecordsOptions?, CancellationToken)
```

`AppendStream` writes to one stream. `AppendRecords` writes to several **atomically** — the whole
set lands or none of it does.

```csharp
var records = new[] { AppendRecord.Create(new OrderPlaced("order-123")) };
var result  = await client.Streams.AppendStream("orders-123", records, ExpectedStreamState.NoStream);
```

`ExpectedStreamState` is the optimistic concurrency guard: `NoStream`, `Exists`, `Deleted`,
`Tombstoned`. A mismatch throws rather than overwriting.

## Read

```csharp
IAsyncEnumerable<ReadMessage> ReadStream(StreamName, ReadStreamOptions, CancellationToken)
IAsyncEnumerable<ReadMessage> ReadRecords(ReadRecordsOptions, CancellationToken)
ValueTask<Record>             InspectRecord(LogPosition, bool skipDecoding, CancellationToken)
ValueTask<Record>             FirstRecord(StreamName, bool skipDecoding, CancellationToken)
ValueTask<Record>             LastRecord(StreamName, bool skipDecoding, CancellationToken)
```

`ReadStream` reads one stream; `ReadRecords` reads the log. Both yield `ReadMessage`, which is
either a `Record` or a `Checkpoint`:

```csharp
await foreach (var message in client.Streams.ReadStream("orders-123")) {
    if (message.Value is Record record)
        Console.WriteLine(record.Value);
}
```

These are `IAsyncEnumerable` over a channel — enumerate once.

## Subscribe

```csharp
IAsyncEnumerable<SubscribeMessage> Subscribe(SubscribeOptions, CancellationToken)
```

A live subscription. For continuous work prefer a [processor](/processors/), which owns the
checkpointing and resilience this leaves to you.

## Manage

```csharp
ValueTask<StreamInfo>  GetStreamInfo(StreamName, CancellationToken)
ValueTask<LogPosition> Delete(StreamName, Revision, CancellationToken)
ValueTask<LogPosition> Tombstone(StreamName, Revision, CancellationToken)
ValueTask<Success>     Truncate(StreamName, Revision, CancellationToken)
ValueTask<Revision>    SetCustomMetadata(StreamName, Metadata, Revision, CancellationToken)
ValueTask<Revision>    ConfigureDataRetention(StreamName, StreamRetentionOptions, Revision, CancellationToken)
```

`Delete` is soft — the stream can be written again. `Tombstone` is permanent and the name can never
be reused.

## Filters

`RecordFilter` is an abstract record; its static members are the factory:

| Factory | Selects |
|---|---|
| `RecordFilter.None` | everything |
| `ByStream`, `ByStreamPattern` | one stream, or a pattern |
| `ByCategory` | a stream category |
| `BySchema` | a schema name |
| `ByUserIndex` | a secondary index |
| `ByExpression` | a SQL-like expression |

```csharp
RecordFilter.ByExpression("schema_name = 'OrderPlaced' OR schema_name = 'OrderShipped'")
```

An expression is parsed, and the server-routable part is pushed down; the rest is evaluated on the
client.

:::caution
A pure equality filter on `stream` routes to a single stream read rather than a log scan. Use `LIKE`,
`IN`, or a compound expression when you mean to filter across the log.
:::
