---
title: Writing a Processor
description: The contracts, the contexts, and what each hook is for.
---

## The record shape

Implement `IRecordProcessor` when you handle one record at a time:

```csharp
public sealed class OrderProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        switch (context.Record.Value) {
            case OrderPlaced placed:   return Handle(placed);
            case OrderShipped shipped: return Handle(shipped);
            default:                   return ValueTask.CompletedTask;
        }
    }
}
```

`context.Record.Value` is the decoded message. The SDK resolves the CLR type from the record's schema
name through your catalog, so you pattern-match on your own types rather than on raw bytes.

`context` also carries a `Logger` scoped to the processor and the record.

## The batch shape

Implement `IRecordBatchProcessor` when the work is cheaper per batch, such as a bulk insert or a
single outbound call:

```csharp
public sealed class BulkIndexer : IRecordBatchProcessor {
    public async ValueTask ProcessBatch(BatchContext context) {
        var documents = context.Records.Select(ToDocument).ToArray();
        await _search.IndexManyAsync(documents, context.CancellationToken);
    }
}
```

Batch size and window are set through `opts.Batching`.

## Lifecycle hooks

All three have defaults on `IProcessor`. Override only what you need.

```csharp
public ValueTask OnInitialize(InitializeContext context) {
    // Once, before the subscription opens. Open resources here.
}

public ValueTask OnConnected(ConnectedContext context) {
    // The subscription is live.
}

public ValueTask OnCheckpoint(CheckpointContext context) {
    // A position was committed. Flush anything that must survive a restart.
}
```

A processor that implements `IAsyncDisposable` or `IDisposable` is disposed on the way out, so
resources opened in `OnInitialize` have somewhere to go.

## Failure

Throw from a handler and the run ends. The processor stops, and the host stops. The position is not
committed past the record that threw. A stop by Ctrl+C or SIGTERM is not a failure.

The resilience options govern the subscription, not your handler. A handler exception is never
retried.

[Error Handling](/kurrent-surge/processors/error-handling/) covers the retry policy, stopping deliberately, and
what happens when a checkpoint fails.

## Running it

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddKurrentSurge(surge => surge.AddProcessor<OrderProcessor>());

await builder.Build().RunAsync();
```

[Running a Processor](/kurrent-surge/processors/running-a-processor/) covers the connection, the
configuration, the processor identity, the health check, and the exit code.

## Under dependency injection

`AddKurrentSurge` registers the processor as a hosted service, so the host starts it and stops it.

```csharp
builder.Services.AddKurrentSurge(surge => {
    surge.Connection("kurrentdb://localhost:2113");
    surge.AddProcessor<OrderProcessor>(opts =>
        opts.Subscription.Filter = RecordFilter.ByCategory("orders"));
});
```

`AddProcessor<T>` registers `T` in the container, keyed by the processor id, so you do not register
it. The dependencies of `T` are yours to register. `AddBatchProcessor` takes the same arguments for
`IRecordBatchProcessor`.
