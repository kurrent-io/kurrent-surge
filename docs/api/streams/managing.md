---
order: 3
---

# Managing streams

Stream management operations include retrieving stream information, deleting streams (soft and permanent), and truncating historical events.

## Get stream info

Retrieve metadata and state for a stream:

```csharp
using Kurrent.Client.Streams;

var info = await client.Streams.GetStreamInfoAsync("order-123");

Console.WriteLine($"State: {info.State}");
Console.WriteLine($"Last revision: {info.LastStreamRevision}");
Console.WriteLine($"Last position: {info.LastStreamPosition}");
Console.WriteLine($"Last append: {info.LastStreamAppendTime}");
```

### StreamInfo properties

| Property | Type | Description |
|----------|------|-------------|
| `State` | `StreamState` | `Active`, `Deleted`, `Tombstoned`, or `Missing` |
| `LastStreamRevision` | `StreamRevision` | Revision of the most recent event |
| `LastStreamPosition` | `LogPosition` | Global log position of the most recent event |
| `LastStreamAppendTime` | `DateTime` | Timestamp of the last append |
| `Metadata` | `StreamMetadata` | Stream metadata (max age, max count, ACLs, etc.) |
| `MetadataRevision` | `StreamRevision` | Revision of the metadata stream |
| `HasMetadata` | `bool` | Whether custom metadata has been set |

### Stream states

| State | Description |
|-------|-------------|
| `Active` | Stream exists and can be read/written |
| `Deleted` | Soft-deleted — can be recreated by appending new events |
| `Tombstoned` | Permanently deleted — cannot be recreated |
| `Missing` | Stream has never existed |

## Delete a stream (soft delete)

Soft-deleting a stream removes it from reads but allows recreation:

```csharp
var position = await client.Streams.DeleteAsync("order-123");

Console.WriteLine($"Deleted at position: {position}");
```

After a soft delete:
- The stream no longer appears in reads
- Appending new events to the same stream name **recreates** it
- The new stream starts at the next revision (history is lost)

### With revision check

Verify the stream is at an expected revision before deleting:

```csharp
var position = await client.Streams.DeleteAsync("order-123", expectedRevision: 9);
```

Fails with `StreamRevisionConflictException` if the current revision does not match.

## Tombstone a stream (permanent delete)

Tombstoning permanently deletes a stream. It can never be recreated:

```csharp
var position = await client.Streams.TombstoneAsync("order-123");
```

After a tombstone:
- The stream cannot be read
- Any attempt to append to the stream fails with `StreamTombstonedException`
- The stream name is permanently reserved

### With revision check

```csharp
var position = await client.Streams.TombstoneAsync("order-123", expectedRevision: 9);
```

::: warning
Tombstoning is irreversible. The stream name can never be reused. Use soft delete if you may need to recreate the stream later.
:::

## Truncate a stream

Truncation removes events before a specified revision. Events at or after the truncation point are preserved:

```csharp
await client.Streams.TruncateAsync("order-123", truncateRevision: 50);
```

This removes all events with revisions 0 through 49. Events at revision 50 and above remain readable.

Truncation is useful for:
- Enforcing data retention policies
- Removing processed events that are no longer needed
- Reducing storage for high-volume streams

## Stream metadata

### Set metadata

Configure stream behavior with metadata:

```csharp
var metadata = new StreamMetadata {
    MaxCount = 1000,
    MaxAge = TimeSpan.FromDays(30)
};

await client.Streams.SetStreamMetadataAsync("order-123", metadata);
```

### Metadata properties

| Property | Type | Description |
|----------|------|-------------|
| `MaxCount` | `long?` | Maximum number of events retained in the stream |
| `MaxAge` | `TimeSpan?` | Maximum age of events — older events are scavenged |
| `TruncateBefore` | `StreamRevision?` | Events before this revision are hidden |
| `CacheControl` | `TimeSpan?` | Cache TTL hint for HTTP clients |
| `Acl` | `StreamAcl?` | Access control list |
| `CustomMetadata` | `JsonDocument?` | Arbitrary user-defined metadata |

### Data retention

Configure automatic data retention with max count and max age:

```csharp
await client.Streams.ConfigureStreamDataRetentionAsync(
    "order-123",
    new StreamDataRetentionOptions {
        MaxCount = 500,
        MaxAge = TimeSpan.FromDays(7)
    }
);
```

Events exceeding these limits are removed during scavenging.

### Access control lists

Control who can read, write, and manage a stream:

```csharp
var metadata = new StreamMetadata {
    Acl = new StreamAcl {
        ReadRoles = ["admin", "reader"],
        WriteRoles = ["admin"],
        DeleteRoles = ["admin"],
        MetaReadRoles = ["admin"],
        MetaWriteRoles = ["admin"]
    }
};

await client.Streams.SetStreamMetadataAsync("order-123", metadata);
```

## Delete vs Tombstone

| | Soft Delete | Tombstone |
|---|-------------|-----------|
| **Reversible** | Yes — append to recreate | No — permanent |
| **Stream name** | Can be reused | Reserved forever |
| **Read after** | Stream not found | Stream tombstoned error |
| **Append after** | Creates new stream | Fails with error |
| **Use case** | Temporary removal, lifecycle management | Permanent removal, compliance |

## Error handling

The idiomatic `Async` methods throw on failure. For explicit control, use the Result-based API:

```csharp
var result = await client.Streams.Delete("order-123");

result.Switch(
    position => Console.WriteLine($"Deleted at {position}"),
    error => error.Switch(
        streamNotFound     => Console.WriteLine("Stream does not exist"),
        streamDeleted      => Console.WriteLine("Stream already deleted"),
        streamTombstoned   => Console.WriteLine("Stream was tombstoned"),
        revisionConflict   => Console.WriteLine("Revision mismatch"),
        exception          => Console.WriteLine($"Unexpected: {exception.Message}")
    )
);
```

### Error types by operation

| Operation | Error Type | Possible Errors |
|-----------|-----------|-----------------|
| `GetStreamInfo` | `GetStreamInfoError` | `StreamNotFoundException`, `Exception` |
| `Delete` | `DeleteStreamError` | `StreamNotFoundException`, `StreamDeletedException`, `StreamTombstonedException`, `StreamRevisionConflictException`, `Exception` |
| `Tombstone` | `TombstoneStreamError` | `StreamNotFoundException`, `StreamDeletedException`, `StreamTombstonedException`, `StreamRevisionConflictException`, `Exception` |
| `Truncate` | `TruncateStreamError` | `StreamNotFoundException`, `StreamDeletedException`, `StreamTombstonedException`, `Exception` |
