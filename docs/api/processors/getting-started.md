---
order: 1
---

# Getting Started with Kurrent.Processors

Kurrent.Processors is an event processing framework for the Kurrent Platform. It provides a structured runtime for consuming records from KurrentDB with automatic checkpointing and lifecycle management. You implement a processor that handles records, and the framework takes care of connecting, subscribing, checkpointing, and graceful shutdown.

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

## Core concepts

A **processor** is your code that handles records. The framework delivers records, manages checkpoints to track progress, and notifies you of lifecycle events. If the processor stops and restarts, it resumes from the last checkpoint rather than reprocessing everything from the beginning.

The main types you interact with are:

- `IRecordProcessor`: the interface you implement to handle records and react to lifecycle events.
- `KurrentProcessor`: the entry point. `KurrentProcessor.Create(...)` produces a configured runner you call `RunAsync` on.
- `ProcessorOptions`: pure data record that holds connection, subscription, and checkpoint configuration. You mutate it through the `Action<ProcessorOptions>` callback passed to `Create`.
- `MessageProcessor`: a delegate-based `IRecordProcessor` for inline typed handlers without subclassing.

## The IRecordProcessor interface

`IRecordProcessor` defines five lifecycle methods. Only `ProcessRecord` is required. The others have default no-op implementations.

```csharp
public interface IRecordProcessor {
    // Required: called when a record is available for processing.
    ValueTask ProcessRecord(ProcessRecordContext context);

    // Optional: called once after the drone connects, before any records are dispatched.
    ValueTask OnConnected(ConnectedContext context) => ValueTask.CompletedTask;

    // Optional: called when a checkpoint is signaled, before state is persisted.
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

    // Optional: called when the drone encounters an unrecoverable error.
    ValueTask OnError(ErrorContext context) => ValueTask.CompletedTask;

    // Optional: called when the drone completes gracefully (including cancellation).
    ValueTask OnComplete(CompleteContext context) => ValueTask.CompletedTask;
}
```

### ProcessRecord

Called each time a record is ready. The `ProcessRecordContext` provides:

- `Record`: the `Record` to process
- `Progress`: a `DroneProgress` snapshot with throughput metrics, checkpoint state, and per-partition positions
- `Logger`: an `ILogger` instance scoped to the drone
- `CancellationToken`: signals when processing should stop
- `StopProcessing()`: call to immediately stop the drone
- `StopBeforeTrack(reason?)` / `StopAfterTrack(reason?)`: cooperative stop variants
- `Checkpoint(reason?)`: request an immediate checkpoint

### OnConnected

Called once after the drone connects to the server, before any records are dispatched. Use this for one-time setup (state tables, opening external resources, priming caches). Failures propagate as fatal startup errors.

### OnCheckpoint

Called when the framework signals a checkpoint, before the checkpoint is persisted. Use this to flush external state. The `CheckpointContext` exposes:

- `CheckpointId`: unique identifier for this checkpoint
- `Trigger`: what triggered it (`Interval`, `CaughtUp`, `Shutdown`, `Rebalance`, or `Savepoint`)
- `Timestamp`: when the checkpoint was triggered
- `Positions`: partition positions captured at signal time

### OnError

Called when the drone hits an unrecoverable error, before transitioning to `Error` status. The `ErrorContext` carries:

- `Error`: the exception that caused the failure
- `Progress`: final progress snapshot at the time of the error

### OnComplete

Called when the drone completes gracefully (including cancellation). The `CompleteContext` carries:

- `Progress`: final progress snapshot at completion

All lifecycle contexts inherit from `DroneContext`, which adds `ProcessorId`, `DroneId`, `SessionId`, `Logger`, and `CancellationToken`.

## Creating a processor by implementing IRecordProcessor

```csharp
using Kurrent.Client.Streams;
using Kurrent.Processors;

public class OrderProcessor : IRecordProcessor {
    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        context.Logger.LogInformation(
            "Processing {Type} from {Stream} at position {Position}",
            context.Record.TypeName,
            context.Record.Stream,
            context.Record.Position
        );

        await HandleRecordAsync(context.Record, context.CancellationToken);
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
            "Completed: {Records} records in {Elapsed} ({Rps:F1} records/sec)",
            throughput.TotalRecords,
            throughput.Elapsed,
            throughput.RecordsPerSecond
        );
        return ValueTask.CompletedTask;
    }

    static Task HandleRecordAsync(Record record, CancellationToken ct) =>
        Task.CompletedTask;
}
```

## Creating a processor with MessageProcessor

For simpler scenarios, use `MessageProcessor` to register strongly-typed handlers inline. `KurrentProcessor.Create(Action<MessageProcessor>, ...)` builds a `MessageProcessor` for you:

```csharp
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Kurrent.Processors.Models;

await KurrentProcessor.Create(handlers => handlers
        .Process<OrderPlaced>(async (msg, ctx) => {
            await HandleOrderPlacedAsync(msg, ctx.CancellationToken);
        })
        .Process<OrderShipped>((msg, ctx) => {
            ctx.Logger.LogInformation("Shipped {OrderId}", msg.OrderId);
        })
        .OnCheckpoint(ctx => {
            ctx.Logger.LogInformation("Checkpoint {Id}", ctx.CheckpointId);
        })
        .OnError(ctx => {
            ctx.Logger.LogError(ctx.Error, "Processing failed");
        }),
    opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId                  = "my-processor";
        opts.Subscription.Filter          = RecordFilter.ByStreamPrefix("order-");
        opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
    })
    .RunAsync(stoppingToken);
```

`MessageProcessor` also lets you register a fallback for records whose runtime type does not match any registered handler:

```csharp
.Fallback(ctx => ctx.Logger.LogWarning("Unhandled: {Type}", ctx.Record.TypeName))
```

You must register at least one `Process<T>` handler or a `Fallback`. The lifecycle hooks (`OnCheckpoint`, `OnError`, `OnComplete`) are optional and have both sync and async overloads.

## Configuring a processor

`KurrentProcessor.Create(processor, configure)` takes an optional `Action<ProcessorOptions>` callback. `ProcessorOptions` is a record, so you set properties directly or use the convenience helpers.

### Connection

```csharp
KurrentProcessor.Create(new OrderProcessor(), opts => {
    opts.Connection("kurrentdb://localhost:2113");
});
```

`Connection` has overloads for tuning the parsed client options or supplying a pre-built `KurrentClientOptions`:

```csharp
KurrentProcessor.Create(new OrderProcessor(), opts => {
    opts.Connection(
        "kurrentdb://admin:changeit@localhost:2113",
        client => client.Resilience = KurrentClientResilienceOptions.Subscription);
});
```

```csharp
KurrentProcessor.Create(new OrderProcessor(), opts => {
    opts.Connection(myClientOptions);
});
```

Each drone always builds and owns its own `KurrentClient`. Client instances are never shared between drones.

### Processor and drone identity

```csharp
opts.ProcessorId = "order-processor";
opts.DroneId     = Environment.MachineName;
```

`ProcessorId` is used for checkpoint coordination. Multiple drones with the same `ProcessorId` share a checkpoint stream. If you don't set it explicitly, it is derived from the processor type name (e.g., `OrderProcessor` becomes `order-processor`).

`DroneId` identifies this drone instance within the group. If left blank, it is auto-generated as `{ProcessorId}-{short-guid}` when `ProcessorId` is set.

### Filtering

Filter records server-side via `Subscription.Filter`:

```csharp
opts.Subscription.Filter = RecordFilter.ByStream("orders-123");
opts.Subscription.Filter = RecordFilter.ByStreamPattern(@"^orders-\d+$");
opts.Subscription.Filter = RecordFilter.BySchema("OrderPlaced");
opts.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'OrderPlaced'");
```

### Initial position

Control where processing starts when no checkpoint exists:

```csharp
using Kurrent.Processors.Models;

// Default: start from the latest record (live tail)
opts.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;

// Process all historical records first
opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
```

### Worker settings

```csharp
// Records to buffer in memory (default: 1000, max: 10000)
opts.BufferCapacity = 5000;

// Skip record deserialization for metadata-only processing
opts.SkipDecoding = true;

// Re-throw all exceptions and emit extra diagnostics
opts.Debug = true;
```

### Checkpointing

`Checkpointing` exposes a small fluent helper for the common knobs:

```csharp
opts.Checkpointing(c => c
    .Interval(TimeSpan.FromSeconds(10))
    .RetentionCount(5)
    .FailurePolicy(CheckpointFailurePolicy.Fail));
```

Or set them directly:

```csharp
opts.Checkpoint.Interval       = TimeSpan.FromSeconds(10);
opts.Checkpoint.RetentionCount = 5;
opts.Checkpoint.FailurePolicy  = CheckpointFailurePolicy.Fail;
```

## Running a processor

The simplest way to run a processor:

```csharp
await KurrentProcessor.Create(new OrderProcessor(), opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId = "order-processor";
    })
    .RunAsync(stoppingToken);
```

`RunAsync` blocks until the stopping token is cancelled or an error occurs.

For console applications, `RunWithGracefulShutdownAsync` automatically handles `Ctrl+C` (SIGINT) and `SIGTERM`, giving the drone time to complete a final checkpoint:

```csharp
await KurrentProcessor.Create(new OrderProcessor(), opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId = "order-processor";
    })
    .RunWithGracefulShutdownAsync(shutdownTimeout: TimeSpan.FromSeconds(15));
```

## Dependency injection

For ASP.NET Core and hosted-service scenarios, register processors with `AddKurrentProcessor<T>`. Each registration becomes its own hosted service. The configure callback receives a `ProcessorOptions` (and optionally an `IServiceProvider`) to set up the connection and behavior:

```csharp
using Kurrent.Client;
using Kurrent.Client.Streams;
using Kurrent.Processors;

var builder = WebApplication.CreateBuilder(args);

// Register the processor in DI so the hosted service can resolve it.
builder.Services.AddSingleton<OrderProcessor>();

// Hook it up as a hosted service.
builder.Services.AddKurrentProcessor<OrderProcessor>(opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = RecordFilter.ByExpression("schema_name IN ('OrderPlaced', 'OrderShipped')");
    opts.Checkpointing(c => c.Interval(TimeSpan.FromSeconds(10)));
});

var app = builder.Build();
app.Run();
```

The bare `AddKurrentProcessor<T>(...)` overload resolves `T` via `sp.GetRequiredService<T>()`. Register the processor type yourself (typically as a singleton).

If you would rather hand the runtime an instance or a factory, there are matching overloads:

```csharp
// Pre-built instance
builder.Services.AddKurrentProcessor(new OrderProcessor(), opts =>
    opts.Connection("kurrentdb://localhost:2113"));

// Factory invoked once at host startup
builder.Services.AddKurrentProcessor(sp => new OrderProcessor(sp.GetRequiredService<ILogger<OrderProcessor>>()),
    opts => opts.Connection("kurrentdb://localhost:2113"));
```

### Connection-string resolution

If your configure callback does not call `opts.Connection(...)`, the registration looks up a connection string from `IConfiguration`:

1. `ConnectionStrings:{ProcessorId}`
2. `ConnectionStrings:kurrentdb` (the default Aspire resource name)

If neither is present, the drone build throws at host startup.

### Configuration binding

Settings under `Kurrent:Processors:{TypeName}` in `IConfiguration` are bound onto `ProcessorOptions` before your configure callback runs. The callback always wins, so configuration acts as defaults:

```json
{
  "Kurrent": {
    "Processors": {
      "OrderProcessor": {
        "ProcessorId": "order-processor",
        "BufferCapacity": 5000,
        "Checkpoint": { "Interval": "00:00:10" },
        "Subscription": { "InitialPosition": "Earliest" }
      }
    }
  },
  "ConnectionStrings": {
    "kurrentdb": "kurrentdb://localhost:2113"
  }
}
```

## Complete example

```csharp
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Kurrent.Processors.Models;

public class OrderProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        context.Logger.LogInformation(
            "[{Type}] Stream={Stream} Position={Position}",
            context.Record.TypeName,
            context.Record.Stream,
            context.Record.Position
        );
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

await KurrentProcessor.Create(new OrderProcessor(), opts => {
        opts.Connection("kurrentdb://localhost:2113?tls=false");
        opts.ProcessorId                  = "order-processor";
        opts.Subscription.Filter          = RecordFilter.ByStreamPrefix("order-");
        opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
        opts.Checkpointing(c => c.Interval(TimeSpan.FromSeconds(10)));
    })
    .RunWithGracefulShutdownAsync();
```

This processor will:

1. Connect to a local KurrentDB instance.
2. Subscribe to all streams starting with `order-`.
3. Process records from the beginning (`Earliest`).
4. Automatically checkpoint every 10 seconds.
5. Perform a final checkpoint on graceful shutdown.
6. Resume from the last checkpoint on restart.
