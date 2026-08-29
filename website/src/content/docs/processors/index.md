---
title: Processors
description: Write two methods. The runtime handles the subscription, checkpoints, retries and partitions.
---

A processor replaces hand-written subscription code. You do not manage a subscription, a checkpoint,
a retry loop or a partition assignment. You write the handler.

This is a complete program:

```csharp
using Kurrent.Surge.Streaming;

await KurrentProcessor.Create(new TemperatureMonitorProcessor(), opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'SensorReading'");
}).RunWithGracefulShutdownAsync();
```

And this is the processor it runs:

```csharp
public sealed class TemperatureMonitorProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is SensorReading reading)
            Evaluate(reading, context.Logger);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnCheckpoint(CheckpointContext context) {
        context.Logger.LogInformation("Checkpoint {Id}", context.CheckpointId);
        return ValueTask.CompletedTask;
    }
}
```

## Why not subscribe directly

The client can subscribe. Doing it by hand means you also own the position you resume from, the
commit of that position, the behaviour on a handler exception, the back-off, the distribution of
work across instances, and the shutdown that does not lose the last batch.

A processor owns all of it. That is the trade: you give up control of the loop and get the loop
written correctly.

## What you implement

`IProcessor` supplies three lifecycle hooks, each with a default, so you override only what you
need:

| Hook | When |
|---|---|
| `OnInitialize` | once, before the subscription opens |
| `OnConnected` | when the subscription is live |
| `OnCheckpoint` | when a position is committed |

On top of it, implement one of two shapes:

| Interface | Method | Use it for |
|---|---|---|
| `IRecordProcessor` | `ProcessRecord(ProcessRecordContext)` | one record at a time |
| `IRecordBatchProcessor` | `ProcessBatch(BatchContext)` | a batch at a time |

## What the runtime handles

Configured through `ProcessorOptions`:

| Concern | Option |
|---|---|
| What to read | `Subscription`, which holds the filter and the start position |
| Position tracking | `Checkpoint` |
| Batch shape | `Batching` |
| Failure handling | `Resilience` |
| Concurrency | `MaxParallelism`, default 1 |
| Observability | `Observers`, `MeterFactory`, `TrackSourceLag` |

Read on for [writing a processor](/processors/writing-a-processor/),
[checkpointing](/processors/checkpointing/), [partitioning](/processors/partitioning/) and
[batching](/processors/batching/).
