---
title: Observability
description: The metrics a processor emits, the lifecycle seam behind them, and what carries a trace id.
---

A processor emits metrics through `System.Diagnostics.Metrics`, so anything that reads a .NET meter
reads these: OpenTelemetry, Prometheus through the OTLP exporter, or `dotnet-counters` at a terminal.

```
Meter name: Kurrent.Surge.Client.Processors
```

## The metrics

| Metric | Kind | Tags |
|---|---|---|
| `kurrent_processor_records_processed` | counter | `processor_id`, `drone_id` |
| `kurrent_processor_record_duration_seconds` | histogram | `processor_id` |
| `kurrent_processor_batch_duration_seconds` | histogram | `processor_id` |
| `kurrent_processor_batch_size` | histogram | `processor_id` |
| `kurrent_processor_checkpoints_completed` | counter | `processor_id`, `drone_id`, `trigger` |
| `kurrent_processor_checkpoints_failed` | counter | `processor_id`, `drone_id`, `trigger` |
| `kurrent_processor_checkpoint_duration_seconds` | histogram | |
| `kurrent_processor_drone_errors` | counter | |
| `kurrent_processor_uncommitted_records` | gauge | |
| `kurrent_processor_uncommitted_bytes` | gauge | |
| `kurrent_processor_source_lag_bytes` | gauge | opt in |
| `kurrent_processor_source_lag_seconds` | gauge | opt in |

`record_duration_seconds` means different things on the two dispatch paths. On the per-record path it
is the true per-record time. On the batch path it is the batch duration divided by the committed
count, so it stays comparable across both.

The `trigger` tag on the checkpoint counters carries which condition fired: an interval, a filter, a
caught-up signal, a manual call, a shutdown, or a rebalance.

## Lag

The `uncommitted_*` gauges are free, because the processor already knows how much it has taken and
not yet committed.

Source lag is opt in.

```csharp
opts.TrackSourceLag = true;
```

Each refresh costs an extra unfiltered backward read of `$all` to find the head, which is why it is
off by default. Turn it on when "how far behind are we" is a question you actually ask, and leave it
off on a processor you already know keeps up.

## Wiring a meter

The processor takes an `IMeterFactory`.

```csharp
opts.MeterFactory = meterFactory;
```

Under dependency injection this is supplied for you. Outside it, note that .NET ships no public
`IMeterFactory` implementation, so a processor built by hand needs one from a host or a test double.

## The observer seam

Metrics are built on a lifecycle seam that is public in its own right. An observer sees the same
events the metrics do, which makes it the place for logging, tracing, or anything else that needs the
processor's internal transitions.

```csharp
opts.Observers.Add(new MyObserver());
```

| Hook | Fires when |
|---|---|
| `OnStateChanged` | The processor changes state |
| `OnRecordReady` | A record is about to be dispatched |
| `OnRecordCompleted` | A record finished |
| `OnRecordSkipped` | `CanProcess` rejected a record |
| `OnBatchReady` | A batch is about to be dispatched |
| `OnBatchCompleted` | A batch finished |
| `OnCheckpointCommitted` | A position was committed |
| `OnCheckpointFailed` | A commit failed |
| `OnSourceLagSampled` | The lag probe read the head |

Every hook has a default, so implement only the ones you want.

:::caution
Observers run **synchronously on the processing thread**, in registration order. Work done in a hook
is work the processor is not doing. Keep them to recording a value or enqueuing to something else,
and never block on I/O inside one.
:::

## Tracing

A processor emits spans through `System.Diagnostics`, so any OpenTelemetry setup picks them up by
subscribing to the source.

```
ActivitySource: Kurrent.Surge.Processors
```

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource("Kurrent.Surge.Processors")
    .AddOtlpExporter());
```

| Span | Covers |
|---|---|
| `processor.process_record` | One record through your handler |
| `processor.process_batch` | One batch through your handler |
| `process {stream}` | A record, named by the stream it came from |

Spans start only when something is listening, so an application with no tracing configured pays
nothing.

### Correlation across a stream

A record carries `$traceId` and `$spanId` in its [metadata](/kurrent-surge/streams/metadata/) when the producer set
them. That links a consumer's work back to whatever caused the record, across process boundaries and
across time, which a span alone cannot do once the producing process is gone.

```csharp
if (context.Record.Metadata.TryGetValue<string>("$traceId", out var traceId))
    using var scope = context.Logger.BeginScope("{TraceId}", traceId);
```
