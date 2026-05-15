---
order: 4
---

# Tracing and OpenTelemetry

<CopyMarkdownButton />

`Kurrent.Processors` emits OpenTelemetry-compatible activity spans through the `System.Diagnostics` API. Any listener that subscribes to the right source name receives them — including the standard OpenTelemetry .NET SDK. The processor ships a small set of public helpers for trace-context propagation and span enrichment, but no listener is registered by default; if nothing subscribes, span creation is a no-op.

`Kurrent.Client` does not declare its own `ActivitySource`, so client operations (appends, reads, registry calls) do not produce spans. Everything described on this page lives in `Kurrent.Processors`.

## Activity source

All processor activities are emitted under a single source:

| Setting     | Value                |
|-------------|----------------------|
| Source name | `Kurrent.Streaming`  |
| Version     | `0.9.0`              |

Subscribe to it through whichever tracing pipeline you use. With the OpenTelemetry .NET SDK:

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("Kurrent.Streaming")
    .AddOtlpExporter()
    .Build();
```

In an ASP.NET Core or generic host:

```csharp
builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Kurrent.Streaming")
        .AddOtlpExporter());
```

## What gets emitted automatically

The drone starts one activity per record it dispatches to your processor:

| Property          | Value                       |
|-------------------|-----------------------------|
| Name              | `processor.process_record`  |
| Kind              | `Consumer`                  |
| Started           | Before `ProcessRecord` runs |
| Disposed          | After the handler returns   |

Default tags on every span:

| Tag                  | Source                            |
|----------------------|-----------------------------------|
| `processor.id`       | `ProcessorOptions.ProcessorId`    |
| `processor.drone_id` | `ProcessorOptions.DroneId`        |

On successful completion the drone adds:

| Tag                    | Description                            |
|------------------------|----------------------------------------|
| `record.total_records` | Running total processed by this drone. |

The span status is set to `Ok` on success. On failure, status is set to `Error` and an `exception` event is added following OTel semantic conventions (`exception.type`, `exception.message`). If your handler throws and the scope is disposed without calling `Complete`, the span is marked as error with description `"Scope disposed without completion"`.

The drone does **not** automatically attach record-level tags (stream, revision, schema). It also does **not** set a parent context from the incoming record. Both are opt-in — see below.

## Continuing a trace across produce and consume

To stitch producer and consumer traces together, the producer writes the trace context into the record's metadata and the consumer extracts it inside the handler.

### On the producer side

Append-time injection uses the `InjectTraceContext` extension on `Metadata`. It reads `Activity.Current` (or an explicit `Activity`/`ActivityContext`) and writes two keys:

| Metadata key | Holds                              |
|--------------|------------------------------------|
| `$traceId`   | `Activity.TraceId` as a hex string |
| `$spanId`    | `Activity.SpanId` as a hex string  |

```csharp
using Kurrent.Diagnostics;

var metadata = Metadata.Empty
    .With("tenant", tenantId)
    .InjectTraceContext();   // pulls from Activity.Current if present

await client.Streams.Append(
    "orders-123",
    new OrderPlaced(orderId, total),
    metadata: metadata
);
```

`InjectTraceContext()` is a no-op when no ambient activity is recording, and never overwrites a key that already exists. Pass an explicit `Activity` or `ActivityContext` if you need to inject from somewhere other than `Activity.Current`.

### On the consumer side

The drone starts `processor.process_record` with no parent. To make your handler-level activity a child of the producer's span, extract the context from the record and start a new activity yourself:

```csharp
using System.Diagnostics;
using Kurrent.Diagnostics;

public ValueTask ProcessRecord(ProcessRecordContext context) {
    var parent = context.Record.ExtractTraceContext();

    using var activity = MyActivitySource.StartActivity(
        "order.handle",
        ActivityKind.Consumer,
        parentContext: parent ?? default
    );

    return HandleAsync(context.Record, context.CancellationToken);
}
```

`ExtractTraceContext()` returns `null` when the keys are missing or malformed, so it's safe to call on every record.

## Enriching the drone span with record metadata

To add stream, revision, and schema tags to the drone's `processor.process_record` span, use `Activity.Current` inside `ProcessRecord` and the `ToTracingTags()` helper:

```csharp
using Kurrent.Diagnostics;

public ValueTask ProcessRecord(ProcessRecordContext context) {
    if (Activity.Current is { } current) {
        foreach (var (key, value) in context.Record.ToTracingTags())
            current.SetTag(key, value);
    }

    return HandleAsync(context.Record, context.CancellationToken);
}
```

`ToTracingTags()` returns a collection following OTel messaging conventions plus Kurrent-specific keys:

| Tag                         | Value                          |
|-----------------------------|--------------------------------|
| `messaging.system`          | `"kurrentdb"`                  |
| `messaging.message.id`      | Record ID.                     |
| `kurrent.record.stream`     | Record's stream name.          |
| `kurrent.record.revision`   | Record's stream revision.      |
| `kurrent.record.position`   | Log position.                  |
| `kurrent.record.schema`     | Schema name.                   |
| `kurrent.record.timestamp`  | Record timestamp.              |

## Linking batch consumption to multiple producers

When processing a `RecordBatch`, individual records may come from different traces. Use `ExtractTraceLinks()` to build a collection of `ActivityLink` for every record with a valid context — useful for batch-level spans that should reference each upstream trace:

```csharp
using System.Diagnostics;
using Kurrent.Diagnostics;

var links = batch.ExtractTraceLinks();   // null when no records have context

using var activity = MyActivitySource.StartActivity(
    "orders.process_batch",
    ActivityKind.Consumer,
    parentContext: default,
    tags: null,
    links: links
);
```

`ExtractTraceLinks()` returns `null` rather than an empty list when no record carries trace context, so passing it straight into `StartActivity` avoids an allocation in the common case.

## Adding your own spans inside a processor

For ad-hoc spans in handler code, declare your own `ActivitySource` and use it as you would in any .NET service. `Kurrent.Diagnostics` also exposes two helpers that wrap the standard pattern:

```csharp
using Kurrent.Diagnostics;

static readonly ActivitySource Source = new("MyApp.Orders");

await Source.Trace("orders.persist", opts => opts
        .Kind(ActivityKind.Internal)
        .Tag("order.id", orderId))
    .RunAsync(async ctx => {
        ctx.Activity.AddEvent("validation.passed");
        await SaveAsync(order);
    });
```

`Trace(...)` configures an activity and returns a `TraceOperation` you execute via `RunAsync` / `Run`. The wrapper sets `Ok` status on success and records the exception (status `Error`, `exception` event) on throw.

For two-phase patterns where success and failure are decided after the work begins, use `BeginTrace`:

```csharp
using var scope = Source.BeginTrace("orders.persist", opts => opts.Tag("order.id", orderId));

try {
    await SaveAsync(order);
    scope.SetTag("order.persisted", true).Complete();
}
catch (Exception ex) {
    scope.Fail(ex);
    throw;
}
```

Disposing a `TraceScope` without calling `Complete` or `Fail` marks the span as error — the safety net catches missed completion paths.

## Diagnostic events

In addition to activities, the drone publishes typed payloads through a `DiagnosticListener` on the same source name (`Kurrent.Streaming`). These are the same lifecycle and progress events surfaced through `ILogger`, but available as strongly-typed records for in-process observers (metrics, custom dashboards). Subscribe via `DiagnosticListener.AllListeners` when you want to react to events without going through the OTel pipeline.

Events emitted include `DroneStartingEvent`, `DroneProcessingEvent`, `RecordProcessedEvent`, `CheckpointInitiatedEvent`, `CheckpointCompletedEvent`, `CheckpointFailedEvent`, `DroneStoppingEvent`, `DroneStoppedEvent`, and `DroneErrorEvent`. Each carries the processor and drone identifiers plus event-specific data.
