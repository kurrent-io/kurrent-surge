---
order: 1
---

# Getting Started with Kurrent.Processors

Kurrent.Processors is an event processing framework for the Kurrent Platform. It provides a structured runtime for consuming records from KurrentDB with automatic checkpointing, batched delivery, and lifecycle management. You implement a processor that handles records, and the framework takes care of connecting, subscribing, batching, checkpointing, and graceful shutdown.

## Installation

Install the `Kurrent.Processors` NuGet package:

::: code-tabs#shell

@tab dotnet CLI

```bash
dotnet add package Kurrent.Processors
```

@tab Package Manager

```powershell
Install-Package Kurrent.Processors
```

:::

`Kurrent.Processors` depends on `Kurrent.Client`, which is included automatically.

## Core Concepts

A **processor** is your code that handles records. The framework delivers records in batches, manages checkpoints to track progress, and notifies you of lifecycle events. If the processor stops and restarts, it resumes from the last checkpoint rather than reprocessing everything from the beginning.

The main types you interact with are:

- `IRecordProcessor` -- the interface you implement to handle records and lifecycle events
- `KurrentProcessors` -- the static entry point for creating and configuring processors
- `KurrentProcessorBuilder` -- the fluent builder returned by `KurrentProcessors.Create` for configuring connection, filtering, batching, and running the processor
- `RecordProcessorBuilder` -- a delegate-based alternative for defining a processor inline without implementing a class

## The IRecordProcessor Interface

`IRecordProcessor` defines four lifecycle methods. Only `ProcessRecords` is required -- the others have default no-op implementations that you can override as needed.

```csharp
public interface IRecordProcessor {
    // Required: Called when a batch of records is available for processing.
    ValueTask ProcessRecords(ProcessRecordsContext context);

    // Optional: Called when a checkpoint is signaled, before state is persisted.
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

    // Optional: Called when the worker encounters an unrecoverable error.
    ValueTask OnError(ErrorContext context) => ValueTask.CompletedTask;

    // Optional: Called when the worker completes gracefully (including cancellation).
    ValueTask OnComplete(CompleteContext context) => ValueTask.CompletedTask;
}
```

### ProcessRecords

This is the only method you must implement. The framework calls it each time a batch of records is ready. The `ProcessRecordsContext` provides:

- `Records` -- a `RecordBatch` (implements `IReadOnlyList<Record>`) containing the records to process
- `Progress` -- a `WorkerProgress` snapshot with throughput metrics, checkpoint state, and per-partition positions
- `Logger` -- an `ILogger` instance scoped to the worker
- `CancellationToken` -- signals when processing should stop
- `StopProcessing()` -- call this to immediately stop the processor

### OnCheckpoint

Called when the framework signals a checkpoint, before the checkpoint is persisted. Use this for flushing external state, writing to a database, or any operation that should happen atomically with the checkpoint. The `CheckpointContext` includes:

- `CheckpointId` -- the unique identifier for this checkpoint
- `Trigger` -- what triggered the checkpoint (`Interval`, `CaughtUp`, `Shutdown`, `Rebalance`, or `Savepoint`)
- `Timestamp` -- when the checkpoint was triggered

### OnError

Called when the worker encounters an unrecoverable error, before the worker transitions to `Error` status. Use this for error reporting, alerting, or cleanup. The `ErrorContext` includes:

- `Error` -- the exception that caused the failure
- `Progress` -- final progress snapshot at the time of the error

### OnComplete

Called when the worker completes gracefully, including when it is cancelled via the stopping token. The `CompleteContext` includes:

- `Progress` -- final progress snapshot at completion

## Creating a Processor by Implementing IRecordProcessor

Implement the `IRecordProcessor` interface to create a processor class:

```csharp
using Kurrent.Client.Streams;
using Kurrent.Processors;

public class OrderProcessor : IRecordProcessor {
    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            context.Logger.LogInformation(
                "Processing {Schema} from {Stream} at position {Position}",
                record.SchemaName,
                record.StreamName,
                record.Position
            );

            // Your processing logic here
            await HandleRecordAsync(record, context.CancellationToken);
        }
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation(
            "Checkpoint {Id} triggered by {Trigger}",
            context.CheckpointId,
            context.Trigger
        );
        return ValueTask.CompletedTask;
    }

    public ValueTask OnError(ErrorContext context) {
        context.Logger.LogError(
            context.Error,
            "Processor failed after {Records} records",
            context.Progress.Throughput.TotalRecords
        );
        return ValueTask.CompletedTask;
    }

    public ValueTask OnComplete(CompleteContext context) {
        var throughput = context.Progress.Throughput;
        context.Logger.LogInformation(
            "Completed: {Records} records in {Elapsed} ({Rps} records/sec)",
            throughput.TotalRecords,
            throughput.Elapsed,
            throughput.RecordsPerSecond
        );
        return ValueTask.CompletedTask;
    }

    static Task HandleRecordAsync(Record record, CancellationToken ct) {
        // Process the record
        return Task.CompletedTask;
    }
}
```

## Creating a Processor with the Delegate Builder

For simpler scenarios, you can define a processor inline using `KurrentProcessors.Create` with an `Action<RecordProcessorBuilder>`. The builder provides both synchronous and asynchronous overloads for each lifecycle method:

```csharp
using Kurrent.Client.Streams;
using Kurrent.Processors;

await KurrentProcessors.Create(p => p
        .ProcessRecordsAsync(async ctx => {
            foreach (var record in ctx.Records) {
                await HandleAsync(record, ctx.CancellationToken);
            }
        })
        .OnCheckpoint(ctx => {
            ctx.Logger.LogInformation("Checkpoint {Id}", ctx.CheckpointId);
        })
        .OnError(ctx => {
            ctx.Logger.LogError(ctx.Error, "Processing failed");
        })
    )
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("my-processor")
    .RunAsync(stoppingToken);
```

The `RecordProcessorBuilder` methods are:

| Method | Description |
|---|---|
| `ProcessRecords(Action<ProcessRecordsContext>)` | Required. Synchronous record handler. |
| `ProcessRecordsAsync(Func<ProcessRecordsContext, ValueTask>)` | Required. Asynchronous record handler. |
| `OnCheckpoint(Action<CheckpointContext>)` | Optional. Synchronous checkpoint handler. |
| `OnCheckpointAsync(Func<CheckpointContext, ValueTask>)` | Optional. Asynchronous checkpoint handler. |
| `OnError(Action<ErrorContext>)` | Optional. Synchronous error handler. |
| `OnErrorAsync(Func<ErrorContext, ValueTask>)` | Optional. Asynchronous error handler. |
| `OnComplete(Action<CompleteContext>)` | Optional. Synchronous completion handler. |
| `OnCompleteAsync(Func<CompleteContext, ValueTask>)` | Optional. Asynchronous completion handler. |

You must call either `ProcessRecords` or `ProcessRecordsAsync` -- the builder will throw `InvalidOperationException` at build time if neither is set.

## Configuring and Running a Processor

`KurrentProcessors.Create` returns a `KurrentProcessorBuilder` that provides a fluent API for configuring the connection, filtering, batching, checkpointing, and execution.

### Connection

Connect to KurrentDB using a connection string:

```csharp
var builder = KurrentProcessors.Create(new OrderProcessor())
    .ConnectTo("kurrentdb://localhost:2113");
```

For more control over client options:

```csharp
var builder = KurrentProcessors.Create(new OrderProcessor())
    .WithClient("kurrentdb://admin:changeit@localhost:2113?tls=true", opts => opts with {
        Resilience = KurrentClientResilienceOptions.HighAvailability
    });
```

You can also provide an existing `KurrentClient` instance:

```csharp
var builder = KurrentProcessors.Create(new OrderProcessor())
    .UseClient(existingClient);
```

### Processor Identity

Set the processor ID to enable checkpointing and group coordination:

```csharp
builder.WithProcessorId("order-processor");
```

Multiple workers with the same processor ID share checkpoints, enabling coordinated processing across instances. If not set explicitly, the processor ID is auto-generated from the processor type name (e.g., `OrderProcessor` becomes `order_processor`).

Optionally set a worker ID for checkpoint attribution:

```csharp
builder.WithWorkerId(Environment.MachineName);
```

### Filtering

Filter records server-side to reduce data transfer:

```csharp
// Filter by specific streams
builder.WithFilter(RecordFilter.ByStream("orders-123", "orders-456"));

// Filter by stream prefix
builder.WithFilter(RecordFilter.ByStreamPrefix("orders-"));

// Filter by schema (event type)
builder.WithFilter(RecordFilter.BySchema("OrderPlaced", "OrderShipped"));

// Filter by expression
builder.WithFilterExpression("schema_name = 'OrderPlaced'");
```

### Initial Position

Control where processing starts when no checkpoint exists:

```csharp
using Kurrent.Processors.Models;

// Start from the beginning (process all historical records)
builder.WithInitialPosition(SubscriptionInitialPosition.Earliest);

// Start from the end (new records only) -- this is the default
builder.WithInitialPosition(SubscriptionInitialPosition.Latest);
```

### Batching

Configure how records are grouped before delivery:

```csharp
builder.ConfigureBatching(b => b
    .MaxSize(500)                              // Max records per batch (default: 200)
    .Timeout(TimeSpan.FromMilliseconds(500))   // Max wait for a full batch (default: 250ms)
);
```

### Checkpointing

Configure automatic checkpoint behavior:

```csharp
builder.ConfigureCheckpointing(c => c
    .Interval(TimeSpan.FromSeconds(10))    // Time between checkpoints (default: 30s)
    .Timeout(TimeSpan.FromSeconds(15))     // Acknowledgment timeout (default: 30s)
    .RetentionCount(5)                     // Checkpoints to retain (default: 3)
    .FailurePolicy(CheckpointFailurePolicy.FailProcessor)  // default: Continue
);
```

### Worker Settings

Configure worker runtime behavior:

```csharp
builder.ConfigureWorker(w => w
    .BufferCapacity(5000)    // Records to buffer in memory (default: 1000, max: 10000)
    .SkipDecoding()          // Skip record deserialization for metadata-only processing
    .ThrowOnFailure()        // Throw exceptions instead of logging and stopping
);
```

### Running

The simplest way to run a processor:

```csharp
await KurrentProcessors.Create(new OrderProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .RunAsync(stoppingToken);
```

`RunAsync` blocks until the stopping token is cancelled or an error occurs.

For console applications that need automatic signal handling:

```csharp
await KurrentProcessors.Create(new OrderProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .RunWithGracefulShutdownAsync(shutdownTimeout: TimeSpan.FromSeconds(15));
```

`RunWithGracefulShutdownAsync` automatically handles `Ctrl+C` (SIGINT) and `SIGTERM`, giving the processor time to complete a final checkpoint before exiting.

### Building for Runtime Access

If you need runtime access to the worker's status and progress, use `Build` to get an `IProcessorWorker`:

```csharp
var worker = KurrentProcessors.Create(new OrderProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .Build();

Console.WriteLine($"Processor: {worker.ProcessorId}");
Console.WriteLine($"Worker: {worker.WorkerId}");
Console.WriteLine($"Status: {worker.Status}");

await worker.RunAsync(stoppingToken);
```

The `IProcessorWorker` exposes:

| Property | Description |
|---|---|
| `ProcessorId` | Unique identifier of the processor group. |
| `WorkerId` | Unique identifier of this worker instance. |
| `Status` | Current lifecycle status: `Starting`, `Processing`, `Stopped`, or `Error`. |
| `Settings` | The resolved `ProcessorSettings` used by this worker. |

## Dependency Injection

For ASP.NET Core and hosted service scenarios, register processors with the DI container using `AddKurrentProcessor`:

```csharp
using Kurrent.Client;
using Kurrent.Processors.Options;

var builder = WebApplication.CreateBuilder(args);

// Register a shared KurrentDB client
builder.Services.AddKurrentClient("kurrentdb://localhost:2113");

// Register a processor as a hosted service
builder.Services.AddKurrentProcessor<OrderProcessor>(config => config
    .WithFilter("schema_name IN ('OrderPlaced', 'OrderShipped')")
    .ConfigureCheckpointing(c => c.Interval(TimeSpan.FromSeconds(10)))
);

var app = builder.Build();
app.Run();
```

The processor is resolved from DI, so it can accept constructor-injected dependencies. The processor ID is auto-generated from the type name unless explicitly set.

To register multiple processors with a shared connection:

```csharp
builder.Services.AddKurrentProcessors(kurrent => kurrent
    .ConnectTo("kurrentdb://localhost:2113")
    .AddProcessor<OrderProcessor>(c => c.WithFilter("schema_name = 'OrderPlaced'"))
    .AddProcessor<ShippingProcessor>(c => c.WithFilter("schema_name = 'OrderShipped'"))
);
```

## Using IRecordProcessorFactory

When your processor needs a fresh instance for each processing session (e.g., it holds mutable state or requires scoped dependencies), implement `IRecordProcessorFactory`:

```csharp
using Kurrent.Processors;

public class OrderProcessorFactory(ILogger<OrderProcessor> logger) : IRecordProcessorFactory {
    public IRecordProcessor CreateProcessor() => new OrderProcessor(logger);
}

// Usage
await KurrentProcessors.Create(new OrderProcessorFactory(logger))
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .RunAsync(stoppingToken);
```

## Complete Example

Here is a complete console application that processes order events from KurrentDB:

```csharp
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Kurrent.Processors.Models;

public class OrderProcessor : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            context.Logger.LogInformation(
                "[{Schema}] Stream={Stream} Position={Position}",
                record.SchemaName,
                record.StreamName,
                record.Position
            );
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation(
            "Checkpoint {Id} ({Trigger})",
            context.CheckpointId,
            context.Trigger
        );
        return ValueTask.CompletedTask;
    }

    public ValueTask OnComplete(CompleteContext context) {
        var t = context.Progress.Throughput;
        context.Logger.LogInformation(
            "Done: {Records} records, {Rps:F1} rec/s, {Elapsed}",
            t.TotalRecords,
            t.RecordsPerSecond,
            t.Elapsed
        );
        return ValueTask.CompletedTask;
    }
}

// Run the processor
await KurrentProcessors.Create(new OrderProcessor())
    .ConnectTo("kurrentdb://localhost:2113?tls=false")
    .WithProcessorId("order-processor")
    .WithFilter(RecordFilter.ByStreamPrefix("order-"))
    .WithInitialPosition(SubscriptionInitialPosition.Earliest)
    .ConfigureBatching(b => b.MaxSize(100).Timeout(TimeSpan.FromMilliseconds(500)))
    .ConfigureCheckpointing(c => c.Interval(TimeSpan.FromSeconds(10)))
    .RunWithGracefulShutdownAsync();
```

This processor will:

1. Connect to a local KurrentDB instance
2. Subscribe to all streams starting with `order-`
3. Process records from the beginning (`Earliest`)
4. Receive records in batches of up to 100, delivered at most every 500ms
5. Automatically checkpoint every 10 seconds
6. Perform a final checkpoint on graceful shutdown
7. Resume from the last checkpoint on restart
