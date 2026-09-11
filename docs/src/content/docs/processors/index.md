---
title: Processors
description: Write two methods. The runtime handles the subscription, checkpoints, reconnects and batching.
---

A processor replaces hand-written subscription code. You do not manage a subscription, a checkpoint,
a reconnect loop or a partition assignment. You write the handler.

```d2 layout=elk
direction: right

KurrentDB: {
  Streams
  Checkpoints
}

Processor: {
  Subscription -> Batch -> Handler -> Track
  Track -> Subscription: next
}

KurrentDB.Streams -> Processor.Subscription: records
Processor.Track -> KurrentDB.Checkpoints: committed position
```

Everything in that loop except **Handler** is the runtime's job. This is a complete program:

```csharp
using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors;
using Microsoft.Extensions.Hosting;

var builder = SurgeProcessor.CreateBuilder<TemperatureMonitorProcessor>(
    args, options => {
        options.Connection("kurrentdb://localhost:2113");
        options.Subscription.Filter =
            RecordFilter.ByExpression("schema_name = 'SensorReading'");
    });

await builder.Build().RunAsync();
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

[Running a Processor](/kurrent-surge/processors/running-a-processor/) covers the connection, the
options, the processor identity, and the health check.

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

On top of `IProcessor`, implement one of two shapes:

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
| Reconnecting a dropped subscription | `Resilience` |
| Checkpoint partition count | internal, and pinned to 1 in this release |
| [Observability](/kurrent-surge/processors/observability/) | `Observers`, `MeterFactory`, `TrackSourceLag` |

- [Writing a Processor](/kurrent-surge/processors/writing-a-processor/)
- [Running a Processor](/kurrent-surge/processors/running-a-processor/)
- [Checkpointing](/kurrent-surge/processors/checkpointing/)
- [Batching](/kurrent-surge/processors/batching/)
- [Error Handling](/kurrent-surge/processors/error-handling/)
- [Observability](/kurrent-surge/processors/observability/)
