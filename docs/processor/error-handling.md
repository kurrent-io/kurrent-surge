---
order: 3
---

# Error handling

<CopyMarkdownButton />

Kurrent.Processors provides structured error handling through lifecycle callbacks on the `IRecordProcessor` interface. When a drone encounters an unrecoverable error, it invokes `OnError` before transitioning to a terminal state. When it shuts down gracefully (including cancellation), it invokes `OnComplete`. Both callbacks receive rich context objects with progress snapshots, drone identifiers, and a structured logger.

The runtime wraps every processor in a safety wrapper that catches exceptions from your code and converts them into well-typed `DroneException` errors, preventing a misbehaving handler from crashing the drone in unexpected ways.

## Drone lifecycle and error flow

A drone moves through a defined set of statuses during its lifetime:

| Status        | Meaning                                                               |
|---------------|-----------------------------------------------------------------------|
| `Unspecified` | Default/initial value before the drone has been started.              |
| `Starting`    | Connecting, restoring state, preparing to process.                    |
| `Processing`  | Actively processing records.                                          |
| `Stopped`     | Terminal: clean shutdown or cancellation completed.                   |
| `Error`       | Terminal: an unrecoverable error occurred.                            |

When an exception propagates out of `ProcessRecord` or an internal operation, the drone:

1. Cancels the internal pump and timers.
2. Calls `OnError` with an `ErrorContext` containing the exception and a final progress snapshot.
3. Sends a disconnect signal to the server (best-effort).
4. Transitions to `DroneStatus.Error`.
5. Re-throws the exception.

When the drone stops gracefully (cancellation token triggered or stream ends), it:

1. Performs a final checkpoint flush (best-effort).
2. Sends a disconnect signal to the server (best-effort).
3. Calls `OnComplete` with a `CompleteContext` containing the final progress snapshot.
4. Transitions to `DroneStatus.Stopped`.

## DroneContext: the base context

All lifecycle contexts inherit from `DroneContext`, which provides common properties available in every callback:

| Property            | Type                | Description                                                            |
|---------------------|---------------------|------------------------------------------------------------------------|
| `ProcessorId`       | `ProcessorId`       | Identifier of the processor group                                      |
| `DroneId`           | `string`            | Identifier of this specific drone instance                             |
| `SessionId`         | `SessionId`         | Server-assigned session identifier for the current connection          |
| `Logger`            | `ILogger`           | Structured logger scoped to the processor                              |
| `CancellationToken` | `CancellationToken` | Token signaling when the drone is shutting down                        |

The `Logger` is the same logger the runtime uses internally, making it straightforward to correlate your application logs with processor diagnostics.

## ErrorContext

`ErrorContext` extends `DroneContext` with error-specific information:

| Property   | Type            | Description                                                                                |
|------------|-----------------|--------------------------------------------------------------------------------------------|
| `Error`    | `Exception`     | The unrecoverable exception that caused the failure                                        |
| `Progress` | `DroneProgress` | Snapshot of throughput metrics, checkpoint progress, and per-partition positions at the time of failure |

```csharp
public record ErrorContext : DroneContext {
    public required DroneProgress Progress { get; init; }
    public required Exception     Error    { get; init; }
}
```

The `Progress` snapshot lets you understand exactly how far the drone got before failing:

```csharp
public ValueTask OnError(ErrorContext context) {
    var throughput = context.Progress.Throughput;
    var checkpoint = context.Progress.Checkpoint;

    context.Logger.LogError(
        context.Error,
        "Drone {DroneId} failed after processing {Total} records in {Elapsed}. " +
        "Last checkpoint: {LastCheckpoint}, records since: {Pending}",
        context.DroneId,
        throughput.TotalRecords,
        throughput.Elapsed,
        checkpoint.LastCheckpointAt,
        checkpoint.RecordsSinceLastCheckpoint
    );

    return ValueTask.CompletedTask;
}
```

## OnError lifecycle

`OnError` is called when the drone encounters an unrecoverable error: an exception that escapes `ProcessRecord`, `OnConnected`, `OnCheckpoint`, or an internal runtime operation. It is invoked **before** the drone transitions to `DroneStatus.Error`.

This callback is purely informational. You cannot prevent the drone from stopping. The error has already occurred. Use it for:

- **Logging** the failure with full context (progress, partition state, drone identity)
- **Alerting** external systems (PagerDuty, Slack, metrics counters)
- **Recording** the failure in a dead-letter stream or database for later analysis
- **Cleaning up** external resources that the processor was using

```csharp
public async ValueTask OnError(ErrorContext context) {
    // Increment a failure counter for monitoring
    _failureCounter.Increment(new TagList {
        { "processor_id", context.ProcessorId.Value },
        { "drone_id",     context.DroneId }
    });

    // Alert the team
    await _alertService.SendAsync(
        $"Processor {context.ProcessorId} failed: {context.Error.Message}",
        context.CancellationToken
    ).ConfigureAwait(false);
}
```

If `OnError` itself throws an exception, the runtime aggregates both the original error and the `OnError` exception before re-throwing. Your `OnError` handler should avoid throwing. If it does, the original error context may be harder to diagnose.

## CompleteContext

`CompleteContext` extends `DroneContext` with a final progress snapshot:

| Property   | Type            | Description                                                                                |
|------------|-----------------|--------------------------------------------------------------------------------------------|
| `Progress` | `DroneProgress` | Final snapshot of throughput metrics, checkpoint progress, and per-partition positions     |

```csharp
public record CompleteContext : DroneContext {
    public required DroneProgress Progress { get; init; }
}
```

## OnComplete lifecycle

`OnComplete` is called when the drone shuts down gracefully. This includes both explicit cancellation (you cancel the `CancellationToken`) and natural completion (the subscription stream ends). It is invoked **after** the final checkpoint flush and **before** the drone transitions to `DroneStatus.Stopped`.

Use it for:

- **Reporting** final processing statistics
- **Flushing** accumulated state to external systems
- **Logging** a clean shutdown summary

```csharp
public ValueTask OnComplete(CompleteContext context) {
    var throughput = context.Progress.Throughput;

    context.Logger.LogInformation(
        "Drone {DroneId} completed. Processed {Total} records in {Elapsed} ({Rps:F1} records/sec)",
        context.DroneId,
        throughput.TotalRecords,
        throughput.Elapsed,
        throughput.RecordsPerSecond
    );

    return ValueTask.CompletedTask;
}
```

If `OnComplete` throws, the drone transitions to `DroneStatus.Error` instead of `Stopped`, and the exception propagates to the caller of `RunAsync`. Keep your `OnComplete` handler defensive.

## DroneProgress: understanding the progress snapshot

Both `ErrorContext` and `CompleteContext` include a `DroneProgress` snapshot with three components:

### ThroughputMetrics

Aggregate processing statistics:

| Property           | Type             | Description                          |
|--------------------|------------------|--------------------------------------|
| `TotalRecords`     | `long`           | Total records processed              |
| `RecordsPerSecond` | `double`         | Average throughput                   |
| `StartedAt`        | `DateTimeOffset` | When processing started              |
| `Elapsed`          | `TimeSpan`       | Total processing duration            |

### CheckpointProgress

Checkpoint state at the time of the snapshot:

| Property                     | Type                | Description                                  |
|------------------------------|---------------------|----------------------------------------------|
| `TotalCheckpoints`           | `long`              | Total checkpoints completed                  |
| `RecordsSinceLastCheckpoint` | `long`              | Records processed since the last checkpoint  |
| `LastTrigger`                | `CheckpointTrigger` | What triggered the last checkpoint           |
| `LastCheckpointAt`           | `DateTimeOffset?`   | When the last checkpoint occurred, or null   |

### Per-partition positions

`DroneProgress.Partitions` is an `ImmutableDictionary<Partition, PartitionProgress>` where each `PartitionProgress` tracks:

| Property              | Type             | Description                                                  |
|-----------------------|------------------|--------------------------------------------------------------|
| `LastTrackedPosition` | `RecordPosition` | Most recent position processed but not yet checkpointed       |
| `LastFlushedPosition` | `RecordPosition` | Most recent position that has been checkpointed              |

Use `LowestCheckpointPartition` to find the partition furthest behind:

```csharp
var lowest = context.Progress.LowestCheckpointPartition;
if (lowest.HasValue) {
    context.Logger.LogWarning(
        "Partition {Partition} is furthest behind at position {Position}",
        lowest.Value.Partition,
        lowest.Value.Item2.LastFlushedPosition
    );
}
```

## DroneException: the runtime safety wrapper

The runtime automatically wraps every `IRecordProcessor` in a safety wrapper. This wrapper catches exceptions from each lifecycle method and converts them into typed `DroneException` errors with specific error codes:

| Method          | Error code              | Factory                          |
|-----------------|-------------------------|----------------------------------|
| `ProcessRecord` | `PROCESS_RECORD_FAILED` | `DroneException.ProcessRecordFailed` |
| `OnConnected`   | `ON_CONNECTED_FAILED`   | `DroneException.OnConnectedFailed`   |
| `OnCheckpoint`  | `ON_CHECKPOINT_FAILED`  | `DroneException.OnCheckpointFailed`  |
| `OnError`       | `ON_ERROR_FAILED`       | `DroneException.OnErrorFailed`       |
| `OnComplete`    | `ON_COMPLETE_FAILED`    | `DroneException.OnCompleteFailed`    |

The wrapper preserves `OperationCanceledException`. Cancellation always passes through without being wrapped. All other exceptions are caught and re-thrown as `DroneException` with the original exception as the `InnerException`.

This means:

- You never need to worry about your processor crashing the runtime with an untyped exception.
- When catching errors from `RunAsync`, you can pattern match on `DroneException` and inspect its `Code` property.
- The original exception is always available via `InnerException`.

```csharp
try {
    await processor.RunAsync(stoppingToken).ConfigureAwait(false);
}
catch (DroneException ex) {
    logger.LogError(ex, "Drone failed with code {Code}: {Message}", ex.Code, ex.Message);
    // ex.InnerException contains the original exception from your processor
}
```

## Implementing error handling with IRecordProcessor

Implement the `IRecordProcessor` interface to define error handling alongside your processing logic. All lifecycle methods except `ProcessRecord` have default implementations that do nothing, so you only override what you need:

```csharp
public class OrderProcessor : IRecordProcessor {
    readonly IAlertService _alerts;

    public OrderProcessor(IAlertService alerts) {
        _alerts = alerts;
    }

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        await HandleOrderAsync(context.Record, context.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask OnError(ErrorContext context) {
        context.Logger.LogError(
            context.Error,
            "Order processor failed. Records processed: {Total}, last checkpoint: {LastCheckpoint}",
            context.Progress.Throughput.TotalRecords,
            context.Progress.Checkpoint.LastCheckpointAt
        );

        await _alerts.NotifyAsync(
            $"Order processor drone {context.DroneId} encountered an error",
            context.CancellationToken
        ).ConfigureAwait(false);
    }

    public ValueTask OnComplete(CompleteContext context) {
        var throughput = context.Progress.Throughput;

        context.Logger.LogInformation(
            "Order processor completed gracefully. Processed {Records} records over {Elapsed}",
            throughput.TotalRecords,
            throughput.Elapsed
        );

        return ValueTask.CompletedTask;
    }
}
```

## Implementing error handling with MessageProcessor

Use `MessageProcessor` (via `KurrentProcessor.Create(Action<MessageProcessor>, ...)`) to wire up error handling with delegates. Both synchronous (`Action<...>`) and asynchronous (`Func<..., ValueTask>`) overloads are available for each callback.

### Synchronous delegates

```csharp
await KurrentProcessor.Create(handlers => handlers
        .Process<OrderPlaced>((msg, ctx) => HandleOrder(msg))
        .OnError(ctx => {
            ctx.Logger.LogError(
                ctx.Error,
                "Failed after {Records} records",
                ctx.Progress.Throughput.TotalRecords
            );
        })
        .OnComplete(ctx => {
            ctx.Logger.LogInformation(
                "Completed: {Records} records in {Elapsed}",
                ctx.Progress.Throughput.TotalRecords,
                ctx.Progress.Throughput.Elapsed
            );
        }),
    opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId = "order-processor";
    })
    .RunAsync(stoppingToken);
```

### Asynchronous delegates

```csharp
await KurrentProcessor.Create(handlers => handlers
        .Process<OrderPlaced>(async (msg, ctx) => {
            await HandleOrderAsync(msg, ctx.CancellationToken);
        })
        .OnError(async ctx => {
            ctx.Logger.LogError(ctx.Error, "Drone {Drone} failed", ctx.DroneId);

            await alertService.SendAsync(
                $"Processor {ctx.ProcessorId} error: {ctx.Error.Message}",
                ctx.CancellationToken
            ).ConfigureAwait(false);
        })
        .OnComplete(async ctx => {
            await metricsService.RecordCompletionAsync(
                ctx.ProcessorId.Value,
                ctx.Progress.Throughput.TotalRecords,
                ctx.CancellationToken
            ).ConfigureAwait(false);
        }),
    opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId = "order-processor";
    })
    .RunAsync(stoppingToken);
```

## Error recovery patterns

### Catch at the RunAsync boundary

Since `RunAsync` re-throws after calling `OnError`, wrap the call to implement restart or escalation logic:

```csharp
var retries = 0;
var maxRetries = 3;

while (retries < maxRetries) {
    try {
        await KurrentProcessor.Create(new OrderProcessor(), opts => {
                opts.Connection("kurrentdb://localhost:2113");
                opts.ProcessorId = "order-processor";
            })
            .RunAsync(stoppingToken)
            .ConfigureAwait(false);

        break; // Clean shutdown
    }
    catch (DroneException ex) when (!stoppingToken.IsCancellationRequested) {
        retries++;
        logger.LogWarning(
            ex,
            "Drone failed (attempt {Attempt}/{Max}), restarting...",
            retries, maxRetries
        );
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), stoppingToken)
            .ConfigureAwait(false);
    }
}
```

Each restart creates a fresh drone that reconnects and resumes from the last checkpoint. Since checkpoints are persisted to KurrentDB, no records are lost between restarts. At-least-once processing is preserved.

### Separate error reporting from retry logic

Keep your `OnError` handler focused on observability. Let the outer loop handle restarts:

```csharp
public class MonitoredProcessor : IRecordProcessor {
    readonly IMetrics _metrics;

    public MonitoredProcessor(IMetrics metrics) {
        _metrics = metrics;
    }

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        await ProcessAsync(context.Record, context.CancellationToken).ConfigureAwait(false);
    }

    public ValueTask OnError(ErrorContext context) {
        // Report, don't retry. The outer loop handles restarts
        _metrics.IncrementFailures(context.ProcessorId.Value);

        context.Logger.LogError(
            context.Error,
            "Processor {Processor} session {Session} failed at {Records} records",
            context.ProcessorId,
            context.SessionId,
            context.Progress.Throughput.TotalRecords
        );

        return ValueTask.CompletedTask;
    }

    public ValueTask OnComplete(CompleteContext context) {
        _metrics.RecordCompletion(
            context.ProcessorId.Value,
            context.Progress.Throughput.TotalRecords
        );

        return ValueTask.CompletedTask;
    }
}
```

### Inspecting partition progress on failure

When an error occurs, you may want to understand which partitions were affected and how far behind they are:

```csharp
public ValueTask OnError(ErrorContext context) {
    foreach (var (partition, progress) in context.Progress.Partitions) {
        context.Logger.LogWarning(
            "Partition {Partition}: tracked={Tracked}, flushed={Flushed}",
            partition,
            progress.LastTrackedPosition,
            progress.LastFlushedPosition
        );
    }

    var lowest = context.Progress.LowestCheckpointPartition;
    if (lowest.HasValue) {
        context.Logger.LogWarning(
            "Furthest behind: partition {Partition} at flushed position {Position}",
            lowest.Value.Partition,
            lowest.Value.Item2.LastFlushedPosition
        );
    }

    return ValueTask.CompletedTask;
}
```

## Accessing logger and cancellation token

Every context object inherits `Logger` and `CancellationToken` from `DroneContext`. The logger is scoped to the processor type, and the cancellation token reflects the drone's shutdown state.

In `OnError`, the `CancellationToken` may already be cancelled if the error occurred during shutdown. Use `CancellationToken.None` if you need to perform cleanup that must complete regardless:

```csharp
public async ValueTask OnError(ErrorContext context) {
    // Use context.CancellationToken for cooperative work
    context.Logger.LogError(context.Error, "Drone failed");

    // Use CancellationToken.None for must-complete cleanup
    await _deadLetterStore.WriteAsync(
        context.Error,
        context.Progress,
        CancellationToken.None
    ).ConfigureAwait(false);
}
```

In `OnComplete`, the `CancellationToken` is typically cancelled (since graceful shutdown is triggered by cancellation). The runtime internally uses `CancellationToken.None` for the final checkpoint flush to ensure it completes, but your `OnComplete` handler receives the original token. If you need to do work that must finish, use `CancellationToken.None` explicitly.
