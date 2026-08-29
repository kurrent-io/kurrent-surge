---
title: Read
description: Read one stream, read everything, fetch a single record, or follow a live tail.
---

Reading is how data comes back out. Four things you can do:

- **Read one stream**, its records in order, forwards or backwards.
- **Read everything**, every record across every stream, in the order it was written.
- **Fetch a single record**, one record at a known position, or the first or last of a stream.
- **Follow a live tail**, a read that never ends and keeps delivering as records arrive.

Reads are lazy. The server streams results as you consume them, so nothing buffers a whole stream
into memory, and you can stop early by breaking out of the loop.

## Read one stream

```csharp
await foreach (var item in client.Streams.ReadStream("order-123")) {
    item.Switch(
        record    => Console.WriteLine("{0} at {1}", record.TypeName, record.Revision),
        checkpoint => { });
}
```

What comes back is a mix of two things. Most items are records. Some are **checkpoints**, barriers
that mark progress. A `FilterCheckpoint` carries the scan watermark, and a `CaughtUpCheckpoint` says
you have reached the end of the data. They exist so a filtered or slow read can still tell you it is
alive and how far it has scanned.

When you only want the records, drop the checkpoints and collect:

```csharp
List<Record> records = await client.Streams.ReadStream("order-123").ToListAsync();
```

Reach for that on a short stream you intend to hold in memory. Stay with `await foreach` for anything
long.

### Shaping the read

The request controls where you start, which direction you travel, how much you take, and how much
work the client does per record.

| Setting | What it decides |
|---|---|
| `Start` | Where to begin. A revision, or a log position |
| `Direction` | `Forwards` from the start, or `Backwards` from the end |
| `Limit` | Stop after this many records |
| `Filter` | Which records the read returns at all |
| `SkipDecoding` | Leave payloads as raw bytes, when you only need positions and metadata |

Reading newest-first is a backwards read anchored at the end. It is the shape you want for "what
happened most recently":

```csharp
var options = new ReadStreamOptions {
    Start     = Revision.Max,
    Direction = ReadDirection.Backwards,
    Limit     = 10
};

await foreach (var item in client.Streams.ReadStream("order-123", options)) { … }
```

`SkipDecoding` is worth knowing about when you are scanning rather than processing. Deserialising
every payload to count records or find a position is wasted work.

## Read everything

The global log holds every record from every stream, in the order the server committed them. This is
what you read when you are building a projection, feeding a downstream system, or looking for
something without knowing which stream it is in.

```csharp
await foreach (var item in client.Streams.ReadRecords(LogPosition.Earliest)) { … }
```

Start at `Earliest` to replay history, `Latest` to pick up only what comes next, or `From(position)`
to resume from where you stopped. The same shaping settings apply as for a single stream.

Reading the whole log unfiltered is expensive. When you want a slice of it, a
[filter](/client/streams/filters/) narrows it server-side rather than shipping everything to you. A
slice is one category, one schema type, or one indexed field.

## Fetch a single record

Three ways to get exactly one record, when a loop is more than you need.

```csharp
Record at42   = await client.Streams.InspectRecord(LogPosition.From(42));

Record first  = await client.Streams.FirstRecord("order-123");
Record last   = await client.Streams.LastRecord("order-123", skipDecoding: true);
```

`InspectRecord` answers "what is at this position". Reach for it when a checkpoint or a log entry
gave you a position and you want to see what it points at.

`FirstRecord` and `LastRecord` give you the edges of a stream. `LastRecord` is the one you will
actually use: it is how you find the current revision before an append that needs
[optimistic concurrency](/client/streams/append/#guarding-against-concurrent-writes).

Pass no stream and you get the edges of the whole log:

```csharp
Record oldest = await client.Streams.FirstRecord();
Record newest = await client.Streams.LastRecord();
```

`InspectRecord` skips decoding by default, since inspecting a position usually means you want the
metadata. `FirstRecord` and `LastRecord` decode by default.

## Follow a live tail

A subscription is a read that does not finish. It delivers history first, tells you when you have
caught up, then keeps delivering as records are written.

Doing this properly means more than a loop: remembering where you got to, resuming there after a
restart, reconnecting when the server goes away, and not reprocessing everything on every deploy.
That is what a [processor](/processors/) is. It owns the subscription, the checkpoints, and the
reconnection, and leaves you writing a handler.

Use `Subscribe` directly when you genuinely want the raw stream and will handle position tracking
yourself. Reach for a processor otherwise.

## When things go wrong

A read throws when you first iterate it, not when you call it. The call only builds the sequence.
Wrap the `await foreach`, not the line that creates it:

```csharp
try {
    await foreach (var item in client.Streams.ReadStream("order-123", options)) { … }
}
catch (StreamNotFoundException) {
    // surfaced from the enumerator
}
```

| Exception | What happened |
|---|---|
| `StreamNotFoundException` | The stream does not exist, or was soft-deleted |
| `StreamTombstonedException` | The stream was tombstoned |
| `LogPositionNotFoundException` | Nothing lives at that log position |
| `OperationCanceledException` | Your cancellation token fired mid-read |
