---
title: Batching
description: Handle a batch at a time when the work is cheaper in bulk.
---

Reading is always batched underneath. What `IRecordBatchProcessor` changes is whether *your handler*
sees a batch or a record.

## When to use it

Use a batch processor when the cost is per call rather than per record. A bulk insert, a search
index update, and one outbound request carrying many items all qualify. Use a record processor when
each record is independent work.

## Defaults

```csharp
opts.Batching.MaxBatchSize     = 500;
opts.Batching.BatchWindow      = TimeSpan.FromMilliseconds(100);
opts.Batching.BufferMultiplier = 2;
```

A batch closes when it reaches `MaxBatchSize` **or** `BatchWindow` elapses, whichever comes first.
That second condition is what stops a quiet stream from holding records indefinitely.

`BufferMultiplier` sizes the read-ahead buffer relative to the batch, so the next batch is being
filled while the current one is in your handler.

## The handler

```csharp
public sealed class BulkIndexer : IRecordBatchProcessor {
    public async ValueTask ProcessBatch(BatchContext context) {
        var documents = context.Records.Select(ToDocument).ToArray();
        await _search.IndexManyAsync(documents, context.CancellationToken);
    }
}
```

The whole batch succeeds or fails together. Throw and the position is not committed past the batch,
so the runtime redelivers the whole batch. Handlers must be idempotent.

## Tuning

Larger batches trade latency for throughput. A wider window does the same on a sparse stream. Start
with the defaults and change one at a time. 500 records at a 100 ms window is already a high
throughput shape.
