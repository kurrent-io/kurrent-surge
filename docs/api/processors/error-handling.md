---
order: 3
---

# Error handling

Kurrent.Processors provides structured error handling through lifecycle callbacks on the `IRecordProcessor` interface. When a worker encounters an unrecoverable error, it invokes `OnError` before transitioning to a terminal state. When it shuts down gracefully (including cancellation), it invokes `OnComplete`. Both callbacks receive rich context objects with progress snapshots, worker identifiers, and a structured logger.

The runtime wraps every processor in a `SafeRecordProcessor` that catches exceptions from your code and converts them into well-typed `KurrentWorkerException` errors, preventing a misbehaving handler from crashing the worker in unexpected ways.

## Worker lifecycle and error flow

A processor worker moves through a defined set of statuses during its lifetime:

| Status | Meaning |
|--------|---------|
| `Starting` | Connecting, restoring state, preparing to process |
| `Processing` | Actively processing records |
| `Stopped` | Terminal -- clean shutdown or cancellation completed |
| `Error` | Terminal -- an unrecoverable error occurred |

When an exception propagates out of `ProcessRecord` or an internal operation, the worker:

1. Cancels the internal pump and timers
2. Calls `OnError` with an `ErrorContext` containing the exception and a final progress snapshot
3. Sends a disconnect signal to the server (best-effort)
4. Transitions to `ProcessorWorkerStatus.Error`
5. Re-throws the exception

When the worker stops gracefully (cancellation token triggered or stream ends), it:

1. Performs a final checkpoint flush (best-effort)
2. Sends a disconnect signal to the server (best-effort)
3. Calls `OnComplete` with a `CompleteContext` containing the final progress snapshot
4. Transitions to `ProcessorWorkerStatus.Stopped`

## WorkerContext -- the base context

All lifecycle contexts inherit from `WorkerContext`, which provides common properties available in every callback:

| Property | Type | Description |
|----------|------|-------------|
| `ProcessorId` | `ProcessorId` | Identifier of the processor group |
| `WorkerId` | `WorkerId` | Identifier of this specific worker instance |
| `SessionId` | `SessionId` | Server-assigned session identifier for the current connection |
| `Logger` | `ILogger` | Structured logger scoped to the processor |
| `CancellationToken` | `CancellationToken` | Token signaling when the worker is shutting down |

The `Logger` is the same logger the runtime uses internally, making it straightforward to correlate your application logs with processor diagnostics.

## ErrorContext

`ErrorContext` extends `WorkerContext` with error-specific information:

| Property | Type | Description |
|----------|------|-------------|
| `Error` | `Exception` | The unrecoverable exception that caused the failure |
| `Progress` | `WorkerProgress` | Snapshot of throughput metrics, checkpoint progress, and per-partition positions at the time of failure |

```csharp
public record ErrorContext : WorkerContext {
    public required WorkerProgress Progress { get; init; }
    public required Exception      Error    { get; init; }
}
```

The `Progress` snapshot lets you understand exactly how far the worker got before failing. You can inspect throughput, checkpoint state, and individual partition positions:

```csharp
public ValueTask OnError(ErrorContext context) {
    var throughput = context.Progress.Throughput;
    var checkpoint = context.Progress.Checkpoint;

    context.Logger.LogError(
        context.Error,
        "Worker {WorkerId} failed after processing {Total} records in {Elapsed}. " +
        "Last checkpoint: {LastCheckpoint}, records since: {Pending}",
        context.WorkerId,
        throughput.TotalRecords,
        throughput.Elapsed,
        checkpoint.LastCheckpointAt,
        checkpoint.RecordsSinceLastCheckpoint
    );

    return ValueTask.CompletedTask;
}
```

## OnError lifecycle

`OnError` is called when the worker encounters an unrecoverable error -- an exception that escapes `ProcessRecord`, `OnCheckpoint`, or an internal runtime operation. It is invoked **before** the worker transitions to `ProcessorWorkerStatus.Error`.

This callback is purely informational. You cannot prevent the worker from stopping -- the error has already occurred. Use it for:

- **Logging** the failure with full context (progress, partition state, worker identity)
- **Alerting** external systems (PagerDuty, Slack, metrics counters)
- **Recording** the failure in a dead-letter stream or database for later analysis
- **Cleaning up** external resources that the processor was using

```csharp
public async ValueTask OnError(ErrorContext context) {
    // Increment a failure counter for monitoring
    _failureCounter.Increment(new TagList {
        { "processor_id", context.ProcessorId.Value },
        { "worker_id", context.WorkerId.Value }
    });

    // Alert the team
    await _alertService.SendAsync(
        $"Processor {context.ProcessorId} failed: {context.Error.Message}",
        context.CancellationToken
    ).ConfigureAwait(false);
}
```

If `OnError` itself throws an exception, the runtime aggregates both the original error and the `OnError` exception into an `AggregateException` before re-throwing. Your `OnError` handler should avoid throwing -- if it does, the original error context may be harder to diagnose.

## CompleteContext

`CompleteContext` extends `WorkerContext` with a final progress snapshot:

| Property | Type | Description |
|----------|------|-------------|
| `Progress` | `WorkerProgress` | Final snapshot of throughput metrics, checkpoint progress, and per-partition positions |

```csharp
public record CompleteContext : WorkerContext {
    public required WorkerProgress Progress { get; init; }
}
```

## OnComplete lifecycle

`OnComplete` is called when the worker shuts down gracefully. This includes both explicit cancellation (you cancel the `CancellationToken`) and natural completion (the subscription stream ends). It is invoked **after** the final checkpoint flush and **before** the worker transitions to `ProcessorWorkerStatus.Stopped`.

Use it for:

- **Reporting** final processing statistics
- **Flushing** accumulated state to external systems
- **Logging** a clean shutdown summary

```csharp
public ValueTask OnComplete(CompleteContext context) {
    var throughput = context.Progress.Throughput;

    context.Logger.LogInformation(
        "Worker {WorkerId} completed. Processed {Total} records in {Elapsed} ({Rps:F1} records/sec)",
        context.WorkerId,
        throughput.TotalRecords,
        throughput.Elapsed,
        throughput.RecordsPerSecond
    );

    return ValueTask.CompletedTask;
}
```

If `OnComplete` throws, the worker transitions to `ProcessorWorkerStatus.Error` instead of `Stopped`, and the exception propagates to the caller of `RunAsync`. Keep your `OnComplete` handler defensive.

## WorkerProgress -- understanding the progress snapshot

Both `ErrorContext` and `CompleteContext` include a `WorkerProgress` snapshot with three components:

### ThroughputMetrics

Aggregate processing statistics:

| Property | Type | Description |
|----------|------|-------------|
| `TotalRecords` | `long` | Total records processed |
| `RecordsPerSecond` | `double` | Average throughput |
| `StartedAt` | `DateTimeOffset` | When processing started |
| `Elapsed` | `TimeSpan` | Total processing duration |

### CheckpointProgress

Checkpoint state at the time of the snapshot:

| Property | Type | Description |
|----------|------|-------------|
| `TotalCheckpoints` | `long` | Total checkpoints completed |
| `RecordsSinceLastCheckpoint` | `long` | Records processed since the last checkpoint |
| `LastTrigger` | `CheckpointTrigger` | What triggered the last checkpoint |
| `LastCheckpointAt` | `DateTimeOffset?` | When the last checkpoint occurred, or null |

### Per-partition positions

`WorkerProgress.Partitions` is an `ImmutableDictionary<Partition, PartitionProgress>` where each `PartitionProgress` tracks:

| Property | Type | Description |
|----------|------|-------------|
| `LastTrackedPosition` | `LogPosition` | Most recent position processed but not yet checkpointed |
| `LastFlushedPosition` | `LogPosition` | Most recent position that has been checkpointed |

Use `LowestCheckpointPartition` to find the partition furthest behind:

```csharp
var lowest = context.Progress.LowestCheckpointPartition;
if (lowest.HasValue) {
    context.Logger.LogWarning(
        "Partition {Partition} is furthest behind at position {Position}",
        lowest.Value.Partition,
        lowest.Value.Progress.LastFlushedPosition
    );
}
```

## SafeRecordProcessor -- the runtime safety wrapper

The runtime automatically wraps every `IRecordProcessor` in a `SafeRecordProcessor`. This wrapper catches exceptions from each lifecycle method and converts them into typed `KurrentWorkerException` errors with specific error codes:

| Method | Error Code | Exception Factory |
|--------|------------|-------------------|
| `ProcessRecord` | `WORKER_PROCESSRECORD_FAILED` | `KurrentWorkerException.ProcessRecordFailed` |
| `OnCheckpoint` | `WORKER_ON_CHECKPOINT_FAILED` | `KurrentWorkerException.OnCheckpointFailed` |
| `OnError` | `WORKER_ON_ERROR_FAILED` | `KurrentWorkerException.OnErrorFailed` |
| `OnComplete` | `WORKER_ON_COMPLETE_FAILED` | `KurrentWorkerException.OnCompleteFailed` |

The wrapper preserves `OperationCanceledException` -- cancellation always passes through without being wrapped. All other exceptions are caught and re-thrown as `KurrentWorkerException` with the original exception as the `InnerException`.

This means:

- You never need to worry about your processor crashing the runtime with an untyped exception
- When catching errors from `RunAsync`, you can pattern match on `KurrentWorkerException` and inspect its `Code` property
- The original exception is always available via `InnerException`

```csharp
try {
    await worker.RunAsync(stoppingToken).ConfigureAwait(false);
} catch (KurrentWorkerException ex) {
    logger.LogError(ex, "Worker failed with code {Code}: {Message}", ex.Code, ex.Message);
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
            $"Order processor worker {context.WorkerId} encountered an error",
            context.CancellationToken
        ).ConfigureAwait(false);
    }

    public ValueTask OnComplete(CompleteContext context) {
        var throughput = context.Progress.Throughput;

        context.Logger.LogInformation(
            "Order processor completed gracefully. " +
            "Processed {Records} records over {Elapsed}",
            throughput.TotalRecords,
            throughput.Elapsed
        );

        return ValueTask.CompletedTask;
    }
}
```

## Implementing error handling with the delegate builder

Use `RecordProcessorBuilder` via `KurrentProcessors.Create(Action<RecordProcessorBuilder>)` to define error handling with delegates. The builder provides both synchronous and asynchronous overloads for each lifecycle callback:

| Async method | Sync method | Signature |
|-------------|------------|-----------|
| `OnErrorAsync` | `OnError` | `Func<ErrorContext, ValueTask>` / `Action<ErrorContext>` |
| `OnCompleteAsync` | `OnComplete` | `Func<CompleteContext, ValueTask>` / `Action<CompleteContext>` |

### Synchronous delegates

Use the sync overloads when your handler does not need to await anything:

```csharp
await KurrentProcessors.Create(p => p
    .ProcessRecord(ctx => {
        HandleOrder(ctx.Record);
    })
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
    })
)
.ConnectTo("kurrentdb://localhost:2113")
.WithProcessorId("order-processor")
.RunAsync(stoppingToken);
```

### Asynchronous delegates

Use the async overloads when you need to perform asynchronous operations in your handler:

```csharp
await KurrentProcessors.Create(p => p
    .ProcessRecordAsync(async ctx => {
        await HandleOrderAsync(ctx.Record, ctx.CancellationToken).ConfigureAwait(false);
    })
    .OnErrorAsync(async ctx => {
        ctx.Logger.LogError(ctx.Error, "Worker {Worker} failed", ctx.WorkerId);

        await alertService.SendAsync(
            $"Processor {ctx.ProcessorId} error: {ctx.Error.Message}",
            ctx.CancellationToken
        ).ConfigureAwait(false);
    })
    .OnCompleteAsync(async ctx => {
        await metricsService.RecordCompletionAsync(
            ctx.ProcessorId.Value,
            ctx.Progress.Throughput.TotalRecords,
            ctx.CancellationToken
        ).ConfigureAwait(false);
    })
)
.ConnectTo("kurrentdb://localhost:2113")
.WithProcessorId("order-processor")
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
        var worker = KurrentProcessors.Create(new OrderProcessor())
            .ConnectTo("kurrentdb://localhost:2113")
            .WithProcessorId("order-processor")
            .Build();

        await worker.RunAsync(stoppingToken).ConfigureAwait(false);
        break; // Clean shutdown
    } catch (KurrentWorkerException ex) when (!stoppingToken.IsCancellationRequested) {
        retries++;
        logger.LogWarning(
            ex,
            "Worker failed (attempt {Attempt}/{Max}), restarting...",
            retries, maxRetries
        );
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), stoppingToken)
            .ConfigureAwait(false);
    }
}
```

Each restart creates a fresh worker that reconnects and resumes from the last checkpoint. Since checkpoints are persisted to KurrentDB, no records are lost between restarts -- at-least-once processing is preserved.

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
        // Report, don't retry -- the outer loop handles restarts
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
            lowest.Value.Progress.LastFlushedPosition
        );
    }

    return ValueTask.CompletedTask;
}
```

## Accessing logger and cancellation token

Every context object inherits `Logger` and `CancellationToken` from `WorkerContext`. The logger is scoped to the processor type, and the cancellation token reflects the worker's shutdown state.

In `OnError`, the `CancellationToken` may already be cancelled if the error occurred during shutdown. Use `CancellationToken.None` if you need to perform cleanup that must complete regardless:

```csharp
public async ValueTask OnError(ErrorContext context) {
    // Use context.CancellationToken for cooperative work
    context.Logger.LogError(context.Error, "Worker failed");

    // Use CancellationToken.None for must-complete cleanup
    await _deadLetterStore.WriteAsync(
        context.Error,
        context.Progress,
        CancellationToken.None
    ).ConfigureAwait(false);
}
```

In `OnComplete`, the `CancellationToken` is typically cancelled (since graceful shutdown is triggered by cancellation). The runtime internally uses `CancellationToken.None` for the final checkpoint flush to ensure it completes, but your `OnComplete` handler receives the original token. If you need to do work that must finish, use `CancellationToken.None` explicitly.
