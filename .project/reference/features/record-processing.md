# Record Processing Feature Specification

**Version:** 1.0.0
**Last Updated:** November 27, 2025
**Status:** Draft

---

## Overview

Record Processing provides continuous, durable record processing from KurrentDB streams. This is the modern replacement for catch-up subscriptions, providing a processor-based model for handling records in batches.

**Key Characteristics:**
- Runs forever until error, cancellation, or explicit stop
- Always delivers records in batches to the processor
- Receives a server-assigned SubscriptionId for durable tracking
- No total record limit (unlike Stream Reads)

---

## Surface API

```csharp
public partial class StreamsClient {
    // ═══════════════════════════════════════════════════════════════════
    // PROCESS GLOBAL LOG ($all)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts processing records from the global log.
    /// Runs until cancelled, error occurs, or processor requests stop.
    /// </summary>
    public Task Process(ProcessOptions options, IRecordProcessor processor, CancellationToken ct = default);

    /// <summary>
    /// Starts processing records from the global log using a processor factory.
    /// </summary>
    public Task Process(ProcessOptions options, IRecordProcessorFactory factory, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════
    // PROCESS SPECIFIC STREAM
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts processing records from a specific stream.
    /// Runs until cancelled, error occurs, or processor requests stop.
    /// </summary>
    public Task Process(StreamProcessOptions options, IRecordProcessor processor, CancellationToken ct = default);

    /// <summary>
    /// Starts processing records from a specific stream using a processor factory.
    /// </summary>
    public Task Process(StreamProcessOptions options, IRecordProcessorFactory factory, CancellationToken ct = default);
}
```

---

## Options

### ProcessOptions (Global Log)

Options for processing records from the global log (`$all`).

| Property            | Type                | Default             | Valid Range     | Description                                           |
|---------------------|---------------------|---------------------|-----------------|-------------------------------------------------------|
| `InitialPosition`   | `InitialPosition`   | `Earliest`          | Earliest/Latest | Fallback position if no checkpoint.                   |
| `Start`             | `LogPosition?`      | `null`              | -               | Explicit start position. Overrides `InitialPosition`. |
| `BatchSize`         | `int`               | `100`               | 1-10,000        | Maximum records per `ProcessRecords` callback.        |
| `BatchTimeout`      | `TimeSpan`          | `500ms`             | 100ms-5s        | Maximum wait before delivering partial batch.         |
| `BufferCapacity`    | `int`               | `1000`              | 1-10,000        | Internal channel buffer size.                         |
| `Filter`            | `IRecordFilter`     | `RecordFilter.None` | -               | Client-side record filtering.                         |
| `Heartbeat`         | `HeartbeatOptions`  | `Default`           | -               | Server heartbeat configuration.                       |
| `SkipDecoding`      | `bool`              | `false`             | -               | Skip deserialization for raw bytes only.              |
| `CancellationToken` | `CancellationToken` | `None`              | -               | Cancellation support.                                 |

**Not Included (by design):**
- `MaxRecords` - Processors run forever until cancelled
- `Timeout` - No slow consumer protection (backpressure via channel)
- `Direction` - Always forwards for processors

### StreamProcessOptions (Specific Stream)

Extends `ProcessOptions` for processing from a specific stream.

| Property | Type              | Default           | Description                                           |
|----------|-------------------|-------------------|-------------------------------------------------------|
| `Stream` | `StreamName`      | `StreamName.None` | The stream to process. **Required.**                  |
| `Start`  | `StreamRevision?` | `null`            | Explicit start revision. Overrides `InitialPosition`. |

### InitialPosition

```csharp
public enum InitialPosition {
    /// <summary>Start from the most recent record (tail).</summary>
    Latest = 0,
    /// <summary>Start from the first record (head).</summary>
    Earliest = 1
}
```

---

## IRecordProcessor Interface

```csharp
/// <summary>
/// Interface for processing records from a stream.
/// Implement this to handle records in batches.
/// </summary>
public interface IRecordProcessor {
    /// <summary>
    /// Called once when processing starts.
    /// Use to initialize resources, load checkpoints, etc.
    /// </summary>
    ValueTask Initialize(InitializationContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called for each batch of records. Required.
    /// Process records and optionally checkpoint progress.
    /// </summary>
    ValueTask ProcessRecords(ProcessRecordsContext context);

    /// <summary>
    /// Called when a heartbeat arrives (Checkpoint, CaughtUp, FellBehind).
    /// Use to track progress or detect connection issues.
    /// </summary>
    ValueTask OnHeartbeat(HeartbeatContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when processing stops (success, error, or cancellation).
    /// Use to cleanup resources, save final checkpoint, etc.
    /// </summary>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// Factory for creating processor instances.
/// Use when you need a fresh processor for each processing session.
/// </summary>
public interface IRecordProcessorFactory {
    IRecordProcessor CreateProcessor();
}
```

---

## Context Objects

### InitializationContext

```csharp
public record InitializationContext(
    string ProcessId,              // Unique ID for this processing session
    ProcessOptions Options,        // The options used to start processing
    CancellationToken CancellationToken
);
```

### ProcessRecordsContext

```csharp
public record ProcessRecordsContext(
    string ProcessId,
    IReadOnlyList<Record> Records, // The batch of records to process
    TimeSpan? Lag,                 // Time behind live (if known)
    CancellationToken CancellationToken
);
```

### HeartbeatContext

```csharp
public record HeartbeatContext(
    string ProcessId,
    Heartbeat Heartbeat,           // Checkpoint, CaughtUp, or FellBehind
    CancellationToken CancellationToken
);
```

### ShutdownContext

```csharp
public record ShutdownContext(
    string ProcessId,
    ShutdownReason Reason,         // Completed, Cancelled, or Error
    LogPosition FinalPosition,     // Last processed position
    long TotalRecords,             // Total records processed
    TimeSpan Duration,             // Total processing time
    ProcessError? Error,           // Error details if Reason == Error
    Record? LastRecord,            // Last successfully processed record
    CancellationToken CancellationToken
);

public enum ShutdownReason {
    Completed,   // Stream ended (rare for $all)
    Cancelled,   // User requested cancellation
    Error        // Processing failed
}
```

---

## Error Handling

### ProcessError

```csharp
public partial class ProcessError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    LogPositionNotFoundException,
    InitializeException,
    ProcessRecordsException,
    HeartbeatException,
    Exception>;
```

### Processor Exceptions

Wrap errors from user code:

```csharp
public class InitializeException(Exception innerException) : KurrentException;
public class ProcessRecordsException(Exception innerException) : KurrentException;
public class HeartbeatException(Exception innerException) : KurrentException;
```

---

## Usage Examples

### Basic Processing

```csharp
// Process all records from a stream
await client.Streams.Process(
    new StreamProcessOptions {
        Stream = "orders",
        BatchSize = 100,
        BatchTimeout = TimeSpan.FromMilliseconds(500),
        InitialPosition = InitialPosition.Earliest
    },
    new OrderProcessor(),
    ct
);
```

### Resume from Checkpoint

```csharp
// Resume from a known checkpoint
await client.Streams.Process(
    new StreamProcessOptions {
        Stream = "orders",
        Start = lastCheckpoint,  // Resume from here
        BatchSize = 50
    },
    new OrderProcessor(),
    ct
);
```

### With Factory and Filter

```csharp
// Process global log with factory and filter
await client.Streams.Process(
    new ProcessOptions {
        InitialPosition = InitialPosition.Latest,
        Filter = RecordFilter.BySchemaPrefix("Order"),
        BatchSize = 200
    },
    new OrderProcessorFactory(),
    ct
);
```

### Example Processor Implementation

```csharp
public class OrderProcessor : IRecordProcessor {
    private ICheckpointStore _checkpoints;

    public async ValueTask Initialize(InitializationContext context) {
        _checkpoints = await CheckpointStore.OpenAsync();
        Console.WriteLine($"Processor {context.ProcessId} initialized");
    }

    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            // Process the record
            await HandleOrder(record);
        }

        // Checkpoint after batch
        var lastRecord = context.Records[^1];
        await _checkpoints.SaveAsync(lastRecord.Position);
    }

    public ValueTask OnHeartbeat(HeartbeatContext context) {
        if (context.Heartbeat.Type == HeartbeatType.CaughtUp) {
            Console.WriteLine("Caught up to live!");
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnShutdown(ShutdownContext context) {
        Console.WriteLine($"Shutdown: {context.Reason}, processed {context.TotalRecords} records");
        await _checkpoints.DisposeAsync();
    }
}
```

---

## Lifecycle

1. **Start** - Call `Process()` to initiate processing
2. **Confirmation** - Receive `SubscriptionConfirmation` from server with unique ID
3. **Initialize** - `IRecordProcessor.Initialize()` called once
4. **Process Loop** - Continuous cycle of:
   - `ProcessRecords()` for batches of records
   - `OnHeartbeat()` for progress signals
5. **Shutdown** - Processing ends when:
   - Cancellation requested → `ShutdownReason.Cancelled`
   - Error occurs → `ShutdownReason.Error`
   - Stream ends (rare) → `ShutdownReason.Completed`
6. **Cleanup** - `OnShutdown()` called with final statistics

---

## Comparison with Stream Reads

| Aspect            | Stream Reads                         | Record Processing            |
|-------------------|--------------------------------------|------------------------------|
| **Purpose**       | One-time read                        | Continuous processing        |
| **Lifecycle**     | Finite                               | Infinite                     |
| **Output**        | `RecordStream` / `RecordBatchStream` | `IRecordProcessor` callbacks |
| **Batching**      | Method parameter                     | Options property             |
| **Total Limit**   | `MaxRecords`                         | None                         |
| **Server State**  | Stateless                            | SubscriptionId               |
| **First Message** | Data                                 | SubscriptionConfirmation     |

---

## Implementation Notes

- Uses separate `ProcessCore` method (not shared with `ReadCore`)
- First message must be `SubscriptionConfirmation` containing server-assigned ID
- Processor lifecycle: Initialize → ProcessRecords/OnHeartbeat loop → OnShutdown
- Errors in processor callbacks are wrapped in specific exception types
- No timeout protection - backpressure via bounded channel only
- Always processes forwards (no backwards direction)
