---
title: Error handling
description: What happens when a handler throws, what the runtime retries, and where a position stops.
---

Throwing from a handler is how you report failure. The runtime wraps the exception with the operation
that failed, applies the resilience policy, and does not commit a position past the record that threw.

The guarantee that matters: **a failed record is not checkpointed past.** After a restart, processing
resumes at or before it, so the work is retried rather than skipped.

## What the runtime retries

```csharp
opts.Resilience.MaxRetryAttempts = int.MaxValue;              // the default
opts.Resilience.BaseDelay        = TimeSpan.FromMilliseconds(500);
opts.Resilience.MaxDelay         = TimeSpan.FromSeconds(30);
opts.Resilience.ShouldRetry      = ex => ex is not ValidationException;
```

Retries are unlimited by default, with exponential backoff between 500 ms and 30 seconds. A processor
is a long-lived background worker, so giving up permanently on a transient fault is usually worse than
waiting.

`ShouldRetry` narrows that. Return `false` for an exception that retrying cannot fix, and the
processor stops instead of looping on it forever.

```csharp
// A malformed record will be malformed on every attempt. Retrying it forever
// stalls the partition behind it.
opts.Resilience.ShouldRetry = ex => ex is not JsonException;
```

## Cancellation is not failure

Cancellation propagates unwrapped, and a stopping processor is not a failing one. Pass
`context.CancellationToken` into anything that accepts one, and do not catch `OperationCanceledException`
to convert it into a handled error.

```csharp
public async ValueTask ProcessRecord(ProcessRecordContext context) {
    await _http.PostAsync(url, content, context.CancellationToken);
}
```

## Stopping deliberately

`StopProcessing` ends the processor without raising a failure. Use it for a condition that means the
work is done or must not continue, rather than throwing to force a stop.

```csharp
public ValueTask ProcessRecord(ProcessRecordContext context) {
    if (context.Record.Value is MigrationCompleted) {
        context.StopProcessing("migration complete");
        return ValueTask.CompletedTask;
    }
    ...
}
```

## Skipping a record before it is dispatched

`CanProcess` is asked before dispatch. Returning `false` means the record never reaches
`ProcessRecord`, which is cheaper than accepting it and returning early.

```csharp
public bool CanProcess(Record record) => record.Value is OrderPlaced or OrderShipped;
```

## When a checkpoint fails

A checkpoint failure is separate from a processing failure, and it has its own policy.

| Policy | Effect |
|---|---|
| `Continue` | Abort the checkpoint, keep processing. The failed drone's partitions are reassigned. The default |
| `Fail` | Stop all processing. Resuming needs manual intervention |

```csharp
opts.Checkpoint.FailurePolicy = CheckpointFailurePolicy.Fail;
```

`Continue` is the default because a failed commit costs replay, while stopping costs availability.
Choose `Fail` when replaying is more expensive than being down, which is unusual.

## The lifecycle hooks

All three have defaults on `IProcessor`, so override only what you need.

| Hook | When | Use it for |
|---|---|---|
| `OnInitialize` | Once, before the subscription opens | Opening resources, inspecting assigned partitions |
| `OnConnected` | The subscription is live | |
| `OnCheckpoint` | A position was committed | Flushing anything that must survive a restart |

A processor implementing `IAsyncDisposable` or `IDisposable` is disposed on the way out, so resources
opened in `OnInitialize` have somewhere to go.

`InitializeContext` carries more than the name suggests:

| Member | Holds |
|---|---|
| `Partitions` | The partitions this instance owns |
| `InitialPosition` | Fallback for a partition with no committed position |
| `StartPosition` | An explicit override that supersedes every stored position. `LogPosition.Unset` means a normal resume |
| `OwnCheckpoints(RecordPosition[])` | Declares this processor the checkpoint owner |

Declaring checkpoint ownership makes the processor responsible for loading, committing and resetting
its own positions, and the runtime stops writing its checkpoint store. Reach for it only when the
positions must be transactional with your own writes.

## At-least-once, always

Delivery is at-least-once regardless of any setting here. A shorter checkpoint interval reduces how
much is replayed after a crash; it does not remove the replay. A handler must tolerate seeing the
same record twice.

That is a property of the system, not a tuning failure. Make the handler idempotent and the question
stops mattering.
