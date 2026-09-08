---
title: Appender
description: The fastest way to write records to KurrentDB, and what you trade for the speed.
---

The `Appender` is the fastest way to get records into KurrentDB. It exists because
[Append](/kurrent-surge/streams/append/) pays a network round trip for every call, and at volume that
round trip is the whole cost.

Three things make it fast.

**One round trip per batch, not per record.** At the default batch size, five hundred records cost a
single `AppendRecords` call and a single server transaction. Measured on a live server at that
default, the cost per record falls by roughly two orders of magnitude.

**Serialization is flat, and it happens on your thread.** A record is framed once, by the thread that
appended it, and a 512 KB payload costs the same as a 64-byte one because the payload is wrapped
rather than copied. What reaches the flush loop is already bytes, so that loop never does work that
scales with what you sent.

**Ten per cent over hand-rolling the same thing.** Batching `AppendRecords` calls yourself, with no
buffering and no callbacks, is the floor. The Appender costs about ten per cent more than that floor,
and writes the batching for you.

```d2 layout=elk
direction: right

A: your thread
S: serialize
L: lane buffer
B: batch
R: AppendRecords

A -> S -> L -> B -> R
```

## What it is for

If you have used a Kafka producer, this is the same shape. You hand records to a client that buffers
them, batches them, and sends them for you, and you learn each record's outcome later through a
callback or an awaited result rather than from the call that queued it.

The workloads that suit a producer suit this:

- **Ingestion and telemetry**, where records arrive continuously and throughput matters more than
  knowing precisely where any single one landed.
- **Import and backfill**, loading an existing dataset into KurrentDB as fast as the server will take
  it.
- **Replay and migration**, moving large volumes out of another system and into this one.

The Appender is experimental. Its surface can change between releases, and what it does today is
described here.

## Append or Appender

| | `AppendStream` | `Appender` |
|---|---|---|
| Round trips | one per call | one per batch |
| Acknowledgment | returned by the call | delivered to a callback, or an awaited task |
| Guard against a racing writer | `ExpectedStreamState`, consistency checks | none |
| Revision of your record | returned | not available |
| Failure scope | your call | every record in the batch |

Stay on Append when a write has to be guarded against a racing writer, because the Appender cannot
express that. Reach for the Appender when volume matters more than per-record certainty.

## Construct one, and dispose it

The Appender builds and owns its own client, from the connection you give it.

```csharp
var options = new AppenderOptions();
options.Connection("kurrentdb://localhost:2113?tls=false");

await using var appender = new Appender(options);
```

| `Connection` overload | Use it for |
|---|---|
| `Connection(string)` | a connection string, nothing else |
| `Connection(string, Action<KurrentClientOptions>)` | a connection string, then adjust the client |
| `Connection(KurrentClientOptions)` | client options you already built |

Everything on [Configuring the Client](/kurrent-surge/client/configuring-the-client/) applies to the
client the Appender builds, because it is the same client.

Disposing drains every lane, sends the final batches, and disposes that client. An Appender that is
never disposed loses whatever is still buffered, so the `await using` is not decoration.

## Three ways to append

| Call | Where the result arrives | Reach for it when |
|---|---|---|
| `Append(record, Action<AppendResult>)` | a callback, on the flush loop | throughput is the whole point |
| `Append(record, Func<AppendResult, ValueTask>)` | an async callback, awaited by the flush loop | the callback does I/O of its own |
| `AppendAsync(record)` | the returned `ValueTask<AppendResult>` | you want code that reads like a normal await |

The two callback forms complete as soon as the record is buffered. `AppendAsync` completes when the
server acknowledges the batch carrying it, and it allocates a little more per record for the
privilege.

```csharp
await appender.Append(record, result => {
    if (!result.IsSuccess)
        logger.LogError(result.Error, "append failed");
});
```

```csharp
var result = await appender.AppendAsync(record);
```

Callbacks run on the flush loop that owns the lane. Work you do inside one delays the next batch on
that lane, so hand anything slow to something else.

## How a batch is filled

A batch is sent when the first of three thresholds is reached.

| Option | Default | Sends the batch when |
|---|---|---|
| `MaxBatchCount` | 500 | it holds that many records |
| `MaxBatchBytes` | 1 MB | its serialized bytes reach that size |
| `LingerTimeout` | 5 ms | that long passes without filling it |

Byte sizes are exact, not estimated, because every record is serialized before it enters the buffer.

`LingerTimeout` is the latency floor of a lightly loaded appender: a record that arrives alone waits
that long before anything is sent. Shorten it to trade throughput for latency.

The Appender validates its options when you construct it. An out-of-range value throws there, not at
the first append. `MaxBatchCount` accepts 1 to 10,000, `MaxBatchBytes` 1 KB to 100 MB,
`BufferCapacity` 1 to 100,000, `Concurrency` 1 to 32, and `LingerTimeout` anything above zero.

## Ordering

Records with no ordering key go to a single lane, and they are sent in the order you appended them.

An ordering key routes the record to a lane chosen by the hash of that key. Records sharing a key
share a lane and keep their order; records with different keys may sit in different lanes and reach
the server in parallel.

```csharp
var record = new AppendRecord {
    Stream = "order-123",
    Value  = new OrderPlaced("order-123", "Widget", 5)
}.OrderingKey("order-123");
```

The key is stored in the record's metadata under `$ordering-key`.

Key by stream name when order within a stream matters. Two records for the same stream under
different keys land in different lanes, and nothing then holds them in sequence.

`Concurrency` sets how many keyed lanes exist, two by default. Raising it helps only when batches are
starved of records; once batches are filling, the wire saturates before the lanes do.

## Backpressure

`BufferCapacity` bounds each lane's buffer, 10,000 records by default. When a lane is full, `Append`
waits for room rather than returning, so a producer that outruns the server is slowed by it.

`FullMode` accepts only `Wait`. The channel drop modes evict buffered records
without completing their callbacks, which leaves a caller waiting for an acknowledgment that can no
longer arrive.

## Flushing

```csharp
var (flushed, inflight) = await appender.FlushAsync();
```

You get back how many records this call flushed, and how many are still buffered or in flight when it
returns. Other threads can append while the flush runs, so `inflight` is not guaranteed to be zero.

## When a batch fails

Failure is batch-scoped. A rejected batch fails every record in it, including records for other
streams that happened to be batched alongside yours, because one batch is one server transaction.

Every record's callback receives a failed `AppendResult`, and `OnError` fires once for the batch.

```csharp
options.OnError = error => logger.LogError(error.Exception, "batch failed");
```

| `AppendResult` member | What it holds |
|---|---|
| `Status` | `Acknowledged`, `Failed`, or `Dropped` |
| `IsSuccess` | true when `Acknowledged` |
| `Error` | the exception, when `Failed` |
| `Response` | the server response, when `Acknowledged` |

`Dropped` is reserved for a future drop mode. No path produces it today, because a full buffer
applies backpressure instead of discarding anything.

| Exception on the failed result | What happened |
|---|---|
| `AppendConsistencyViolationException` | The server rejected the transaction |
| `MaxAppendRecordSizeExceededException` | One record exceeds the server's per-record limit |
| `MaxAppendTransactionSizeExceededException` | The batch exceeds the server's transaction limit |

Both size limits are measured on the serialized record, and
[Append](/kurrent-surge/streams/append/#when-things-go-wrong) covers what counts toward them. A batch
that grows past the transaction limit is the one to watch: lower `MaxBatchBytes` until it fits.

## What it does not do

There is no `ExpectedStreamState` and no consistency check on this path, because a batch carries
records and nothing else. A write that has to be guarded belongs on `AppendStream`.

`Response` is the batch's response, so its revisions cover every record the batch carried. Nothing
tells you where one particular record landed.
