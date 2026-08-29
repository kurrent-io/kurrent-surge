---
title: Manage
description: Inspect streams, remove data, control retention, and attach your own metadata.
---

Stream management covers everything outside appending and reading. Four things you can do:

- **Inspect a stream**, to find out whether it exists, what state it is in, where it starts and ends.
- **Remove a stream**, soft deleting it so the name can be reused, or tombstoning it permanently.
- **Remove events**, truncating a stream so old records stop being readable while the stream lives on.
- **Configure a stream**, setting retention rules or attaching your own key-value metadata to it.

Retention, access control, and custom metadata all live together on a hidden metadata stream that
sits beside the real one. That is why those operations return a revision, and why concurrent edits
need care. [Concurrency on metadata](#concurrency-on-metadata) covers it.

## Inspect a stream

Before you branch on whether a stream exists, ask. `GetStreamInfo` answers for any stream, including
one that has never existed, and it never throws for a missing one. That makes it the safe way to
check, and the natural building block for admin views and test assertions.

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

if (info.State == StreamState.Missing)
    Console.WriteLine("nothing here yet");
```

A stream is in one of four states: `Active` (readable and writable), `Deleted` (soft-deleted, and
recreatable by appending), `Tombstoned` (gone for good), or `Missing` (never existed).

You get back a `StreamInfo`:

| Member | Type | What it tells you |
|---|---|---|
| `Stream` | `StreamName` | The stream you asked about |
| `State` | `StreamState` | `Active`, `Deleted`, `Tombstoned`, or `Missing` |
| `InfoRevision` | `Revision` | Where the metadata stream is. Hand it back to guard a concurrent edit |
| `Retention` | `StreamRetention` | The retention rules in force: `MaxAge`, `MaxCount`, `TruncateBefore` |
| `Acl` | `StreamAcl` | Who may read and write |
| `Metadata` | `Metadata` | Whatever you attached |
| `FirstStreamRevision` · `FirstStreamPosition` · `FirstStreamAppendedAt` | | Where the stream starts, and when |
| `LastStreamRevision` · `LastStreamPosition` · `LastStreamAppendedAt` | | Where it ends, and when |

## Delete a stream

Deleting a stream hides it from readers while leaving the name free. It is the one you want almost
every time: for a lifecycle that has run its course, for data that has expired, or to undo a stream
created by mistake.

```csharp
LogPosition at = await client.Streams.Delete("order-123");
```

Afterwards the stream reads as though it were never there. Append to the same name and it **comes
back to life**, picking up from where it left off: if the last event was revision `3`, the next one
is `4`, and readers see only `4` onward.

The deletion is itself recorded, so subscribers observe it. The call returns its log position. The
old events survive on disk until the next scavenge clears them.

Delete conditionally when it matters that nothing arrived while you were deciding. That is a common
shape when tidying up a per-job stream after reading its final state:

```csharp
LogPosition at = await client.Streams.Delete("order-123", 9);
```

If someone appended in the meantime, the call fails rather than discarding their write.

:::caution
Deleting is not erasing. KurrentDB keeps one event behind to record that the stream existed and what
its last revision was. That residue matters if you are removing data because you must, for a
subject-erasure request say. Append a `StreamDeleted` event and set `MaxCount` to 1, or use
redaction.
:::

## Tombstone a stream

Tombstoning permanently deletes a stream and leaves a tombstone behind. It is not a firmer delete.
It is a different operation, and it cannot be undone.

```csharp
LogPosition at = await client.Streams.Tombstone("order-123");
```

A tombstone event is appended to the stream, and that event outlives everything else. A scavenge
clears the stream's records, but the tombstone stays forever, which is what keeps the name
permanently reserved. Every later read and every later append fails.

Reach for it when the name itself must never come back: a compliance requirement, an abusive tenant,
or a name you are deliberately retiring. Reach for delete in every other case.

| | Delete | Tombstone |
|---|---|---|
| Can you undo it | yes. Append and the stream returns | no |
| Can the name be reused | yes | never |
| Reading afterwards | `StreamNotFoundException` | `StreamTombstonedException` |
| Appending afterwards | starts the stream again | fails, permanently |
| After a scavenge | records gone | records gone, tombstone remains |
| Reach for it when | the lifecycle ended, data expired, undo | compliance, abuse, retiring a name |

A conditional form exists here too:

```csharp
LogPosition at = await client.Streams.Tombstone("order-123", 9);
```

## Truncate a stream

Truncating removes **events**; deleting and tombstoning remove **streams**. The stream itself stays
active and keeps taking appends. You are declaring that everything before a point no longer
matters.

```csharp
Success ok = await client.Streams.Truncate("order-123", 50);
```

Revisions 0 to 49 stop being readable. Revision 50 onward is untouched. The hidden records are
cleared at the next scavenge.

This is the only way to remove events at all. KurrentDB cannot delete an event from the middle of a
stream, only from the start. It is also the same machinery a delete uses: deleting a stream is
truncating it at its maximum.

It fits whenever history has served its purpose but the stream has not:

- trimming work items off a queue stream once they are processed
- dropping history after snapshotting an aggregate
- enforcing retention by revision, where count and age do not express the rule

## Configure retention

Retention rules let the server decide what to keep, instead of you truncating by hand. Records
falling outside the limits become eligible for removal at the next scavenge.

```csharp
Revision rev = await client.Streams.ConfigureDataRetention("order-123", options => {
    options.MaxCount = 500;
    options.MaxAge   = TimeSpan.FromDays(7);
});
```

| Rule | Effect |
|---|---|
| `MaxCount` | Keep at most this many records |
| `MaxAge` | Keep records younger than this |

Set both and either can retire a record. When only one bound matters, a shorter form says so:

```csharp
await client.Streams.ConfigureDataRetention("order-123", maxCount: 500);
await client.Streams.ConfigureDataRetention("order-123", TimeSpan.FromDays(7));
```

## Attach custom metadata

You can hang arbitrary key-value data off a stream: an owning team, a classification label, a
compliance marker, or per-stream configuration your application reads at startup. It travels with
the stream and comes back on `GetStreamInfo`.

```csharp
Revision rev = await client.Streams.SetCustomMetadata("order-123", Metadata.New
    .With("owner", "billing-team")
    .With("compliance", "pii"));
```

Keys you supply are added or overwritten; keys already there and not mentioned survive.

To remove a key, or to decide what to write based on what is already there, use the callback form.
It reads the current metadata, hands it to you, and writes the result back.

```csharp
await client.Streams.SetCustomMetadata("order-123", meta =>
    meta.With("last-reviewed", DateTimeOffset.UtcNow));
```

### Concurrency on metadata

Retention, access control, and custom metadata share one hidden metadata stream, and it has its own
revision. Two services editing different things are still writing to the same place, so the later
write can silently discard the earlier one. One sets retention while the other tags an owner.

Read the current revision and pass it back to prevent that:

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

await client.Streams.SetCustomMetadata("order-123", metadata, info.InfoRevision);
```

If anyone wrote first, the call fails with `StreamRevisionConflictException` instead of overwriting
them. Both `SetCustomMetadata` and `ConfigureDataRetention` return the metadata stream's new
revision, so you can chain edits without re-reading.

## When things go wrong

| Exception | What happened |
|---|---|
| `StreamNotFoundException` | The stream does not exist |
| `StreamDeletedException` | It was soft-deleted |
| `StreamTombstonedException` | It was tombstoned, and nothing will bring it back |
| `StreamRevisionConflictException` | The stream or its metadata moved before your call landed |
