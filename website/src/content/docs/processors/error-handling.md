---
title: Error handling
description: What happens when a handler throws, what the runtime retries, and where a position stops.
---

Throwing from a handler is how you report failure, and it ends the run. The runtime wraps the
exception, stops the processor, and faults the task you awaited with an `AggregateException` carrying
a `DroneException`. Records later in the batch are never dispatched.

The guarantee that matters: **a failed record is not checkpointed past.** Restart and processing
resumes at or before it, so the work is retried rather than skipped. Restarting is your job, or your
host's.

## What the runtime retries, and what it does not

The resilience options govern the **subscription**, not your handler. They decide how the client
recovers when the transport drops, the server is not ready, or a leader moves.

```csharp
opts.Resilience.MaxRetryAttempts = int.MaxValue;              // the default
opts.Resilience.BaseDelay        = TimeSpan.FromMilliseconds(500);
opts.Resilience.MaxDelay         = TimeSpan.FromSeconds(30);
```

Reconnection is unlimited by default, with exponential backoff between 500 ms and 30 seconds. A
processor is a long-lived background worker, so giving up permanently on a transient transport fault
is usually worse than waiting.

`ShouldRetry` classifies those transport exceptions. It is never asked about one your handler threw.

| Failure | What handles it |
|---|---|
| The subscription drops | The resilience pipeline reconnects, keeping position |
| The leader moves | The same pipeline, after the client re-resolves |
| Your handler throws | Nothing. The processor stops |

So a handler that can fail transiently owns its own retry. Put the policy inside the handler, where
you know what is worth retrying.

```csharp
public async ValueTask ProcessRecord(ProcessRecordContext context) {
    await _pipeline.ExecuteAsync(
        async ct => await _http.PostAsync(url, content, ct),
        context.CancellationToken);
}
```

## No dead-letter queue, by decision

A broker framework answers a poison record with a dead-letter queue and a circuit breaker. Neither
exists here, and that is a decision rather than a gap, because they solve a problem an ordered log
does not have.

A broker acknowledges one message at a time. Moving a single failed message aside leaves the rest
untouched, because nothing ties them together. A processor reads a log, and its progress is one
position. There is no per-record acknowledgement to withhold, so committing past a failed record is
the only way to skip it, and that discards the record permanently while telling nobody.

The runtime does the opposite and keeps the position behind the failure.

| Situation | What the position does |
|---|---|
| Handler succeeds | Advances past the record |
| Handler throws | Stays behind it, so a restart resumes at that record |
| `CanProcess` returns `false` | Advances, because nothing claimed the record |

The last row is the deliberate exception. A record your processor never accepts is not a failure, and
a stream of types you do not handle would otherwise hold the position still forever.

What that leaves you:

| Broker answer | Here |
|---|---|
| Dead-letter a poison message | Nothing. The processor stops with the position behind the record |
| Refuse a message before it is handled | `CanProcess` returns `false` and the record is never dispatched |
| Retry a message in place | Your handler's own policy, inside `ProcessRecord` |

A stopped processor is recoverable and a skipped record is not, so stopping is the safer default.
Moving past a bad record stays a decision you make explicitly: make the handler tolerate it, or
exclude it with `CanProcess`, then restart.

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
