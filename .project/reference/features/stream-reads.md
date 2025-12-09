# Stream Reads Feature Specification

**Version:** 1.0.0
**Last Updated:** November 27, 2025
**Status:** Draft

---

## Overview

Stream Reads provide one-time, finite data retrieval from KurrentDB streams. Users can read individual records or batches from either the global log (`$all`) or specific streams.

---

## Surface API

```csharp
public partial class StreamsClient {
    // ═══════════════════════════════════════════════════════════════════
    // READ RECORDS (Individual)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads individual records from the global log.
    /// </summary>
    public ValueTask<Result<RecordStream, ReadError>> ReadRecords(ReadOptions options);

    /// <summary>
    /// Reads individual records from a specific stream.
    /// </summary>
    public ValueTask<Result<RecordStream, ReadError>> ReadRecords(ReadStreamOptions options);

    // ═══════════════════════════════════════════════════════════════════
    // READ BATCHES
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads records in batches from the global log.
    /// </summary>
    public ValueTask<Result<RecordBatchStream, ReadError>> ReadBatches(
        ReadOptions options,
        int batchSize,
        TimeSpan batchTimeout);

    /// <summary>
    /// Reads records in batches from a specific stream.
    /// </summary>
    public ValueTask<Result<RecordBatchStream, ReadError>> ReadBatches(
        ReadStreamOptions options,
        int batchSize,
        TimeSpan batchTimeout);
}
```

---

## Options

### ReadOptionsBase (Abstract)

Base options shared by all read operations.

| Property            | Type                | Default             | Valid Range        | Description                                                      |
|---------------------|---------------------|---------------------|--------------------|------------------------------------------------------------------|
| `MaxRecords`        | `long`              | `long.MaxValue`     | ≥ 1                | Maximum total records to read. Use for "read first N" scenarios. |
| `BufferCapacity`    | `int`               | `1000`              | 1-10,000           | Internal channel buffer size. Controls backpressure.             |
| `Filter`            | `IRecordFilter`     | `RecordFilter.None` | -                  | Client-side record filtering.                                    |
| `Heartbeat`         | `HeartbeatOptions`  | `Default`           | -                  | Server heartbeat configuration.                                  |
| `Direction`         | `ReadDirection`     | `Forwards`          | Forwards/Backwards | Read direction through stream/log.                               |
| `SkipDecoding`      | `bool`              | `false`             | -                  | Skip deserialization for raw bytes only.                         |
| `CancellationToken` | `CancellationToken` | `None`              | -                  | Cancellation support.                                            |

**Removed Properties:**
- `BatchSize` - Moved to `ReadBatches()` method parameter
- `BatchTimeout` - Moved to `ReadBatches()` method parameter
- `Timeout` - Removed (backpressure via bounded channel only)

### ReadOptions (Global Log)

Extends `ReadOptionsBase` for reading from `$all`.

| Property | Type          | Default              | Description                          |
|----------|---------------|----------------------|--------------------------------------|
| `Start`  | `LogPosition` | `LogPosition.Latest` | Starting position in the global log. |

### ReadStreamOptions (Specific Stream)

Extends `ReadOptionsBase` for reading from a specific stream.

| Property | Type             | Default              | Description                            |
|----------|------------------|----------------------|----------------------------------------|
| `Stream` | `StreamName`     | `StreamName.None`    | The stream to read from. **Required.** |
| `Start`  | `StreamRevision` | `StreamRevision.Min` | Starting revision within the stream.   |

---

## Output Types

### RecordStream

Stream of individual records. Implements `IAsyncEnumerable<ReadMessage>`.

```csharp
[PublicAPI]
public record RecordStream : IAsyncEnumerable<ReadMessage>, IAsyncDisposable {
    /// <summary>
    /// Number of messages currently buffered.
    /// </summary>
    public int QueuedMessages { get; }

    public IAsyncEnumerator<ReadMessage> GetAsyncEnumerator(CancellationToken ct = default);
    public ValueTask DisposeAsync();
}
```

### RecordBatchStream

Stream of record batches. Implements `IAsyncEnumerable<ReadRecordBatchResult>`.

```csharp
[PublicAPI]
public record RecordBatchStream : IAsyncEnumerable<ReadRecordBatchResult>, IAsyncDisposable {
    /// <summary>
    /// Number of messages currently buffered.
    /// </summary>
    public int QueuedMessages { get; }

    public IAsyncEnumerator<ReadRecordBatchResult> GetAsyncEnumerator(CancellationToken ct = default);
    public ValueTask DisposeAsync();
}
```

**Batching Behavior:**
- Batches are yielded when `batchSize` records are accumulated
- OR when `batchTimeout` expires with pending records
- OR when a heartbeat arrives (flushes pending records first, then yields heartbeat)

### Message Types

```csharp
/// <summary>
/// A message from a read stream - either a Record or a Heartbeat.
/// </summary>
public readonly partial record struct ReadMessage : IVariant<Record, Heartbeat>;

/// <summary>
/// A batch result - either an array of Records or a Heartbeat.
/// </summary>
public readonly partial record struct ReadRecordBatchResult : IVariant<Record[], Heartbeat>;
```

---

## Error Handling

```csharp
public partial class ReadError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    LogPositionNotFoundException,
    Exception>;
```

| Error                          | Condition                            |
|--------------------------------|--------------------------------------|
| `StreamNotFoundException`      | Stream does not exist                |
| `StreamDeletedException`       | Stream was soft-deleted              |
| `StreamTombstonedException`    | Stream was hard-deleted (tombstoned) |
| `LogPositionNotFoundException` | Invalid log position specified       |

---

## Usage Examples

### Read Individual Records

```csharp
// Read from a specific stream
await using var stream = await client.Streams.ReadRecords(new ReadStreamOptions {
    Stream = "orders",
    Start = StreamRevision.Min,
    Direction = ReadDirection.Forwards,
    MaxRecords = 100
});

await foreach (var message in stream) {
    message.Switch(
        record => Console.WriteLine($"Record: {record.EventType}"),
        heartbeat => Console.WriteLine($"Progress: {heartbeat.Position}")
    );
}
```

### Read Batches

```csharp
// Read batches from global log
await using var batches = await client.Streams.ReadBatches(
    new ReadOptions {
        Start = LogPosition.Start,
        Filter = RecordFilter.BySchemaPrefix("Order")
    },
    batchSize: 100,
    batchTimeout: TimeSpan.FromMilliseconds(500)
);

await foreach (var batch in batches) {
    batch.Switch(
        records => ProcessBatch(records),
        heartbeat => LogProgress(heartbeat)
    );
}
```

---

## Lifecycle

1. **Start** - Call `ReadRecords()` or `ReadBatches()` to initiate read
2. **Enumerate** - Consume messages via `await foreach`
3. **End** - Stream ends when:
   - End of stream/log reached
   - `MaxRecords` limit reached
   - Cancellation requested
   - Error occurs
4. **Dispose** - Always dispose the stream to release resources

---

## Implementation Notes

- Uses `ReadCore` internal method for all read operations
- No slow consumer timeout - backpressure via bounded channel only
- Channel completion exceptions propagate through enumeration, not disposal
- Single enumeration only - second enumeration throws `InvalidOperationException`
