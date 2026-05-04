---
order: 3
---

# Managing streams

Stream management operations cover inspecting a stream's state, deleting and tombstoning streams, truncating earlier events, configuring data retention, and attaching custom metadata.

## Get stream info

Retrieve metadata and state for a stream:

```csharp
using Kurrent.Client.Streams;

var info = await client.Streams.GetStreamInfo("order-123");

Console.WriteLine($"State:      {info.State}");
Console.WriteLine($"First rev:  {info.FirstStreamRevision}");
Console.WriteLine($"Last rev:   {info.LastStreamRevision}");
Console.WriteLine($"Last write: {info.LastStreamAppendedAt}");
```

### StreamInfo properties

| Property                | Type               | Description                                                                       |
|-------------------------|--------------------|-----------------------------------------------------------------------------------|
| `Stream`                | `StreamName`       | Name of the stream.                                                               |
| `State`                 | `StreamState`      | `Active`, `Deleted`, `Tombstoned`, or `Missing`.                                  |
| `InfoRevision`          | `Revision`         | Revision of the stream's metadata stream. Used for optimistic concurrency on metadata updates. |
| `Retention`             | `StreamRetention`  | Retention configuration (max age, max count, truncate before).                    |
| `Acl`                   | `StreamAcl`        | Access control list applied to the stream.                                        |
| `Metadata`              | `Metadata`         | User-defined custom metadata.                                                     |
| `FirstStreamRevision`   | `Revision`         | Revision of the first record in the stream.                                       |
| `FirstStreamPosition`   | `LogPosition`      | Log position of the first record in the stream.                                   |
| `FirstStreamAppendedAt` | `DateTime`         | Timestamp of the first record.                                                    |
| `LastStreamRevision`    | `Revision`         | Revision of the most recent record.                                               |
| `LastStreamPosition`    | `LogPosition`      | Log position of the most recent record.                                           |
| `LastStreamAppendedAt`  | `DateTime`         | Timestamp of the most recent record.                                              |

### Stream states

| State        | Description                                                |
|--------------|------------------------------------------------------------|
| `Active`     | Stream exists and can be read or written.                  |
| `Deleted`    | Soft-deleted. Can be recreated by appending new events.    |
| `Tombstoned` | Permanently deleted. Cannot be recreated.                  |
| `Missing`    | Stream has never existed.                                  |

## Delete a stream

A soft delete removes the stream from reads while leaving the name available for reuse.

```csharp
var position = await client.Streams.Delete("order-123");

Console.WriteLine($"Deleted at position: {position}");
```

After a soft delete:

- The stream no longer appears in reads.
- Appending new events to the same stream name **recreates** it.
- The new stream starts at the next revision. The previous history is no longer visible.

### With a revision check

Pass an expected revision to abort the delete when the stream has moved on.

```csharp
var position = await client.Streams.Delete("order-123", Revision.From(9));
```

Fails with `StreamRevisionConflictException` if the stream's current revision does not match.

## Tombstone a stream

Tombstoning permanently deletes a stream. The name is reserved forever and cannot be reused.

```csharp
var position = await client.Streams.Tombstone("order-123");
```

After a tombstone:

- The stream cannot be read.
- Any attempt to append to the stream fails with `StreamTombstonedException`.
- The stream name is permanently reserved.

### With a revision check

```csharp
var position = await client.Streams.Tombstone("order-123", Revision.From(9));
```

::: warning
Tombstoning is irreversible. The stream name can never be reused. Use a soft delete if you may need to recreate the stream later.
:::

## Truncate a stream

Truncation hides events before a specified revision. Events at or after the truncation point remain readable.

```csharp
await client.Streams.Truncate("order-123", Revision.From(50));
```

This hides all events with revisions 0 through 49. Events at revision 50 and above remain readable.

Truncation is useful for:

- Enforcing data retention policies.
- Removing processed events that are no longer needed.
- Reducing storage for high-volume streams.

## Configure data retention

`ConfigureDataRetention` sets `MaxCount` and `MaxAge` on a stream. Events that fall outside these limits are removed during scavenging.

### By max count

```csharp
await client.Streams.ConfigureDataRetention("order-123", maxCount: 500);
```

### By max age

```csharp
await client.Streams.ConfigureDataRetention("order-123", maxAge: TimeSpan.FromDays(7));
```

### Both, with a configure callback

```csharp
await client.Streams.ConfigureDataRetention("order-123", options => {
    options.MaxCount = 500;
    options.MaxAge   = TimeSpan.FromDays(7);
});
```

### Both, with explicit options and revision

Pass the current `InfoRevision` for optimistic concurrency. The call fails with `StreamRevisionConflictException` if another writer has changed the metadata stream.

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

await client.Streams.ConfigureDataRetention(
    "order-123",
    new StreamRetentionOptions { MaxCount = 500, MaxAge = TimeSpan.FromDays(7) },
    info.InfoRevision);
```

### StreamRetention properties

| Property         | Type        | Description                                                          |
|------------------|-------------|----------------------------------------------------------------------|
| `MaxAge`         | `TimeSpan?` | Maximum age of events. Older events are scavenged.                   |
| `MaxCount`       | `int?`      | Maximum number of events retained in the stream.                     |
| `TruncateBefore` | `Revision`  | Events before this revision are hidden. Set by `Truncate`.           |

## Set custom metadata

Attach arbitrary user-defined metadata to a stream with `SetCustomMetadata`. Custom metadata is stored alongside the stream's retention and ACL configuration on the metadata stream.

### Replace metadata

```csharp
var metadata = Metadata.New
    .With("owner",      "billing-team")
    .With("compliance", "pii");

await client.Streams.SetCustomMetadata("order-123", metadata);
```

### Update metadata in place

The `Action<Metadata>` overload reads the current metadata, applies your callback, and writes the result. The current `InfoRevision` is used automatically.

```csharp
await client.Streams.SetCustomMetadata("order-123", meta => {
    meta.With("last-reviewed", DateTimeOffset.UtcNow);
});
```

### With an explicit revision

```csharp
var info = await client.Streams.GetStreamInfo("order-123");

await client.Streams.SetCustomMetadata("order-123", metadata, info.InfoRevision);
```

## Delete vs Tombstone

|                  | Soft delete                                  | Tombstone                                |
|------------------|----------------------------------------------|------------------------------------------|
| **Reversible**   | Yes (append to recreate)                     | No (permanent)                           |
| **Stream name**  | Can be reused                                | Reserved forever                         |
| **Read after**   | `StreamNotFoundException`                    | `StreamTombstonedException`              |
| **Append after** | Creates a new stream                         | Fails with `StreamTombstonedException`   |
| **Use case**     | Temporary removal, lifecycle management      | Permanent removal, compliance            |

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
    Console.WriteLine($"Expected {ex.ExpectedRevision}, was {ex.ActualRevision}");
}
catch (StreamTombstonedException) {
    // Stream was permanently deleted
}
```

### Common exceptions

| Exception                         | Cause                                                                       |
|-----------------------------------|-----------------------------------------------------------------------------|
| `StreamNotFoundException`         | Stream does not exist.                                                      |
| `StreamDeletedException`          | Stream was previously soft-deleted.                                         |
| `StreamTombstonedException`       | Stream was permanently deleted.                                             |
| `StreamRevisionConflictException` | Current stream or metadata revision did not match the expected revision.    |
| `OperationCanceledException`      | The cancellation token was cancelled during the operation.                  |
