---
order: 3
---

# Managing streams

<CopyMarkdownButton />

Stream management covers everything outside of appending and reading: inspecting state, removing data, controlling retention, and attaching metadata.

## Get stream info

`GetStreamInfo` is the safe way to inspect a stream. It returns lifecycle state and metadata for any stream, including streams that have never existed, and never throws for missing streams. Reach for it before branching on existence, building admin views, or asserting state in tests.

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

Console.WriteLine("State:      {0}", info.State);
Console.WriteLine("First rev:  {0}", info.FirstStreamRevision);
Console.WriteLine("Last rev:   {0}", info.LastStreamRevision);
Console.WriteLine("Last write: {0}", info.LastStreamAppendedAt);
```

A stream's state is one of `Active` (readable and writable), `Deleted` (soft-deleted, can be recreated by appending), `Tombstoned` (permanently removed), or `Missing` (has never existed).

## Delete vs tombstone

Two ways to remove a stream. Both record the removal so subscribers see it, but the consequences are very different.

|                  | Soft delete                                  | Tombstone                                |
|------------------|----------------------------------------------|------------------------------------------|
| **Reversible**   | Yes (append to recreate)                     | No (permanent)                           |
| **Stream name**  | Can be reused                                | Reserved forever                         |
| **Read after**   | `StreamNotFoundException`                    | `StreamTombstonedException`              |
| **Append after** | Creates a new stream                         | Fails with `StreamTombstonedException`   |
| **Use case**     | Lifecycle, expiry, undo                      | Compliance, abuse, name reservation      |

::: tip
Default to soft delete. Tombstone only when you need a guarantee that the data and the name are gone for good.
:::

## Soft delete

Removes the stream from reads while leaving the name available for reuse. The deletion itself is recorded and the call returns its log position.

```csharp
var position = await client.Streams.Delete("order-123");
```

Appending to the same name afterwards recreates the stream. The new stream starts at the next revision after the deleted history. Old records are no longer visible to readers.

To delete conditionally, pass the revision the stream is expected to be at. The call fails with `StreamRevisionConflictException` if another writer has appended since you last read.

```csharp
var position = await client.Streams.Delete("order-123", Revision.From(9));
```

Use the conditional form when "delete only if nothing new has arrived" matters. A common case is deleting a per-request or per-job stream after you've read its final state.

## Tombstone

Permanently removes a stream. The name is reserved forever. Reads fail with `StreamTombstonedException`. Future appends fail with the same exception.

```csharp
var position = await client.Streams.Tombstone("order-123");
```

A revision check is supported the same way as for soft delete:

```csharp
var position = await client.Streams.Tombstone("order-123", Revision.From(9));
```

::: warning
Tombstoning is irreversible. The name can never be reused on this database. Pick soft delete unless you specifically need this guarantee.
:::

## Truncate

Hides records before a given revision while leaving the stream itself active. Records at or after the truncation point keep responding as normal.

```csharp
await client.Streams.Truncate("order-123", Revision.From(50));
```

This hides revisions 0 through 49. Revision 50 onwards remains readable. Hidden records become eligible for physical removal at the next scavenge.

Truncate is the right tool when records past a certain point are no longer needed but the stream itself is. Common cases:

- Trimming processed work items from a queue stream.
- Discarding history after snapshotting an aggregate.
- Enforcing per-stream retention by revision rather than count or age.

## Configure retention

Sets per-stream retention rules. Records that fall outside the limits become eligible for removal at the next scavenge.

| Field      | Effect                                                |
|------------|-------------------------------------------------------|
| `MaxCount` | Keep at most this many records.                       |
| `MaxAge`   | Keep records younger than this duration.              |

Set one or both:

```csharp
await client.Streams.ConfigureDataRetention("order-123", options => {
    options.MaxCount = 500;
    options.MaxAge   = TimeSpan.FromDays(7);
});
```

Single-field overloads are available when only one bound matters.

::: info Concurrency on metadata
Retention, ACL, and custom metadata are stored on a stream's metadata stream, which has its own revision. Pass the current `InfoRevision` to avoid silently overwriting a concurrent change. The call fails with `StreamRevisionConflictException` if another writer has updated the metadata stream first.

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

await client.Streams.ConfigureDataRetention(
    "order-123",
    new StreamRetentionOptions { MaxCount = 500, MaxAge = TimeSpan.FromDays(7) },
    info.InfoRevision);
```
:::

## Custom metadata

Attach arbitrary key/value data to a stream. It's stored on the stream's metadata stream alongside retention and ACL, and is returned on `GetStreamInfo`. Typical uses include ownership tags, classification labels, compliance markers, and per-stream application config.

::: tabs#metadata-modes

@tab Add or update

```csharp
var metadata = Metadata.New
    .With("owner",      "billing-team")
    .With("compliance", "pii");

await client.Streams.SetCustomMetadata("order-123", metadata);
```

Adds or overwrites the keys you provide and writes at the latest revision. Existing keys not in the bag you pass are preserved.

@tab Update via callback

```csharp
await client.Streams.SetCustomMetadata("order-123", meta => {
    meta.With("last-reviewed", DateTimeOffset.UtcNow);
});
```

Reads the current metadata, applies your function, and writes the result back at the latest revision. Use this when you need imperative control, such as removing a key or applying conditional logic.

@tab Replace with concurrency check

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

await client.Streams.SetCustomMetadata("order-123", metadata, info.InfoRevision);
```

Full replace at a specific revision. Anything not in the new document is dropped. The call fails with `StreamRevisionConflictException` if another writer has updated the metadata stream since you read `InfoRevision`. Use this when concurrent edits to ACL, retention, or metadata could collide.

:::

## Error handling

Management operations throw on failure. Catch the well-typed exceptions when you need to handle specific cases:

```csharp
try {
    await client.Streams.Delete("order-123", Revision.From(9));
}
catch (StreamNotFoundException) {
    // Stream does not exist
}
catch (StreamRevisionConflictException ex) {
    Console.WriteLine("Expected {0}, was {1}", ex.ExpectedRevision, ex.ActualRevision);
}
catch (StreamTombstonedException) {
    // Stream was permanently deleted
}
```

| Exception                         | Cause                                                                       |
|-----------------------------------|-----------------------------------------------------------------------------|
| `StreamNotFoundException`         | Stream does not exist.                                                      |
| `StreamDeletedException`          | Stream was previously soft-deleted.                                         |
| `StreamTombstonedException`       | Stream was permanently deleted.                                             |
| `StreamRevisionConflictException` | Current stream or metadata revision did not match the expected revision.    |
| `OperationCanceledException`      | The cancellation token was cancelled during the operation.                  |

## StreamInfo properties

| Property                | Type               | Description                                                                                    |
|-------------------------|--------------------|------------------------------------------------------------------------------------------------|
| `Stream`                | `StreamName`       | Name of the stream.                                                                            |
| `State`                 | `StreamState`      | `Active`, `Deleted`, `Tombstoned`, or `Missing`.                                               |
| `InfoRevision`          | `Revision`         | Revision of the stream's metadata stream. Used for optimistic concurrency on metadata updates. |
| `Retention`             | `StreamRetention`  | Retention configuration (max age, max count, truncate before).                                 |
| `Acl`                   | `StreamAcl`        | Access control list applied to the stream.                                                     |
| `Metadata`              | `Metadata`         | User-defined custom metadata.                                                                  |
| `FirstStreamRevision`   | `Revision`         | Revision of the first record in the stream.                                                    |
| `FirstStreamPosition`   | `LogPosition`      | Log position of the first record in the stream.                                                |
| `FirstStreamAppendedAt` | `DateTime`         | Timestamp of the first record.                                                                 |
| `LastStreamRevision`    | `Revision`         | Revision of the most recent record.                                                            |
| `LastStreamPosition`    | `LogPosition`      | Log position of the most recent record.                                                        |
| `LastStreamAppendedAt`  | `DateTime`         | Timestamp of the most recent record.                                                           |

## StreamRetention properties

| Property         | Type        | Description                                                          |
|------------------|-------------|----------------------------------------------------------------------|
| `MaxAge`         | `TimeSpan?` | Maximum age of records. Older records are scavenged.                 |
| `MaxCount`       | `int?`      | Maximum number of records retained in the stream.                    |
| `TruncateBefore` | `Revision`  | Records before this revision are hidden by `Truncate`.               |
