---
title: Append
description: Write records to a stream, and guard against writers racing you.
---

Appending is how data gets into KurrentDB. Two things you can do:

- **Write to one stream**, the common case. One record or many, committed together.
- **Write across several streams**, as one atomic transaction spanning streams, when an invariant
  does not fit inside a single one.

Both are transactional: every record in the call lands, or none of them does. Neither needs the
stream to exist first, because the first append creates it.

The other half of appending is deciding what happens when someone else writes at the same time.
[Guarding against concurrent writes](#guarding-against-concurrent-writes) covers that, and it is
usually the part that matters.

## Write to one stream

Name the destination once. Every record in the call inherits it, and the batch commits as a unit.

```csharp
var record = new AppendRecord {
    Stream = "order-123",
    Value  = new OrderPlaced("order-123", "Widget", 5)
};

var result = await client.Streams.AppendStream(record);

Console.WriteLine("now at revision {0}", result.Revision);
```

Payloads take their wire format from the message type unless you set `DataFormat`, and the schema
name comes from your [catalog](/kurrent-surge/schema/catalogs/). See
[Data Formats](/kurrent-surge/schema/formats/). The same shape works for events, commands, and
snapshots, because KurrentDB does not distinguish them.

You get back where the write landed: a `Position`, plus the `Stream` and `Revision` read off it.

Several records to the same stream go in one call, and commit together or not at all:

```csharp
await client.Streams.AppendStream("order-456", new[] {
    new AppendRecord { Value = new OrderPlaced(…) },
    new AppendRecord { Value = new OrderConfirmed(…) }
});
```

## Guarding against concurrent writes

Two services appending to the same stream will eventually collide. `ExpectedStreamState` tells the
server what the stream must look like for your write to be accepted, and the server rejects it
otherwise. This is optimistic concurrency: you do not lock anything, you state an assumption and find
out if it held.

The usual shape is read-then-write. Read where the stream is, decide, and append against that
revision:

```csharp
var last = await client.Streams.LastRecord("order-123", skipDecoding: true);

// … decide what to do, based on what you read …

await client.Streams.AppendStream(record, last.Revision);
```

If anyone appended in between, the call throws `StreamRevisionConflictException` rather than writing
on top of a decision made from stale data. The exception carries both revisions, so you can retry
against the new one.

| Expectation | The write succeeds only if |
|---|---|
| `Any` | always. The default, with no check |
| `NoStream` | the stream does not exist yet |
| `Exists` | the stream already has records |
| `Revision.From(n)` | the stream is at exactly revision `n` |

`NoStream` is how you claim a name exactly once. The first writer wins, and everyone else gets
`StreamAlreadyExistsException`. `Revision.From(n)` is the strong one, and what you want whenever two
writers can contend.

A `Revision` converts to an `ExpectedStreamState` on its own, so `last.Revision` passes straight in.

A tombstoned stream refuses every append no matter what you expect.

## Write across several streams

Sometimes the rule you are enforcing spans two streams: *only record this transfer if the inventory
stream has not moved since I read it.* One append cannot express that, because it can only guard the
stream it writes to.

`AppendRecords` can. Each record names its own destination, and consistency checks assert the state
of any stream the transaction touches, including streams you are not writing to.

```csharp
var inventory = await client.Streams.LastRecord($"inventory-{sku}", skipDecoding: true);

await client.Streams.AppendRecords(
    new[] {
        new AppendRecord { Stream = $"inventory-{sku}", Value = transferred },
        new AppendRecord { Stream = "transfer-log",     Value = transferred }
    },
    new AppendRecordsOptions {
        Checks = [ConsistencyCheck.StreamAtRevision($"inventory-{sku}", inventory.Revision)]
    },
    default);
```

The server evaluates every check against the real state at commit time. One failure rejects the whole
transaction.

| Check | Asserts that the stream |
|---|---|
| `StreamAtRevision(stream, revision)` | sits at exactly that revision |
| `StreamExists(stream)` | has at least one record |
| `StreamDoesNotExist(stream)` | has no records, or was deleted |
| `StreamIsDeleted(stream)` | was soft-deleted |
| `StreamIsTombstoned(stream)` | was tombstoned |

You get back the revision of every stream the transaction touched, plus the log position of the
commit.

Because the checks span streams, a failure cannot collapse into one exception the way a single-stream
conflict does. `AppendConsistencyViolationException` carries every violation, each naming its stream,
what you expected, and what was actually there:

```csharp
catch (AppendConsistencyViolationException ex) {
    foreach (var violation in ex.Violations) {
        var state = violation.AsStreamStateViolation;
        Console.WriteLine("{0}: expected {1}, was {2}",
            state.Stream, state.ExpectedState, state.ActualState);
    }
}
```

## When things go wrong

| Exception | What happened |
|---|---|
| `StreamRevisionConflictException` | Someone wrote first. Carries `ExpectedRevision` and `ActualRevision` |
| `StreamAlreadyExistsException` | You expected `NoStream`, but records were there |
| `StreamNotFoundException` | You expected `Exists`, and it does not |
| `StreamDeletedException` | Your consistency check expected a live stream and the stream was soft-deleted. See below |
| `StreamTombstonedException` | The stream was tombstoned. No append will ever succeed |
| `AppendConsistencyViolationException` | One or more consistency checks failed. See `Violations` |
| `MaxAppendRecordSizeExceededException` | A single record exceeds the server's per-record limit. Carries `RecordActualSize` and `RecordMaxSize` |
| `MaxAppendTransactionSizeExceededException` | The whole transaction exceeds the server's limit. Carries `TransactionActualSize` and `TransactionMaxSize` |

Both sizes are measured on the **serialized record**, not on your payload: the schema name, the
record id and any metadata count toward the total. Budget for that headroom rather than treating the
ceiling as a payload allowance. The client measures the same unit the server enforces, so an
oversized append fails immediately instead of after a round trip.

### Appending to a soft-deleted stream revives it

`StreamDeletedException` is a concurrency outcome, not a rule about where you may write. An append
that carries **no** consistency check succeeds against a soft-deleted stream, and the stream comes
back to life with its history truncated away. You only see the exception when you asked for a
specific state and the server found the stream deleted instead.

Tombstoning is the opposite. It is permanent, and no append to a tombstoned stream ever succeeds.

If you need "write only if this stream is alive", say so with `ExpectedStreamState.Exists` and handle
`StreamDeletedException`. Without a check, the next write silently undoes a delete you did not
expect.
