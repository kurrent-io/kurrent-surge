---
title: Checkpointing
description: Where a processor resumes from, and who decides when a position is committed.
---

A checkpoint is the position a processor resumes from after a restart. The runtime tracks and
commits it for you.

## Defaults

```csharp
opts.Checkpoint.AutoTrack      = true;                    // track each processed position
opts.Checkpoint.AutoCheckpoint = true;                    // commit them on a timer
opts.Checkpoint.Interval       = TimeSpan.FromSeconds(30);
opts.Checkpoint.RetentionCount = 3;
opts.Checkpoint.FailurePolicy  = CheckpointFailurePolicy.Continue;
```

With both switches on, which is the default, you write a handler and never think about position
again.

## The two switches

`AutoTrack` decides whether a processed record advances the tracked position automatically. Turn it
off and you call `context.Track(...)` yourself, which is what you want when a record is handed to
something asynchronous and "processed" does not mean "done".

`AutoCheckpoint` decides whether tracked positions are committed on the interval. Turn it off and
you request a commit explicitly.

They are independent. Tracking is *what* has been processed; checkpointing is *when* that is made
durable.

## Interval

`Interval` is the commit period, 30 seconds by default. A shorter interval means less replay after a
crash and more writes. It is a recovery-time dial, not a correctness one. At-least-once delivery
holds either way, so a handler must tolerate seeing a record twice.

## Failure policy

`CheckpointFailurePolicy.Continue` keeps processing when a commit fails. The alternative stops the
processor. Continue is the default because a failed commit costs replay, while a stop costs
availability.

## OnCheckpoint

The hook fires when a position is committed. It is the moment to flush anything that must survive a
restart, because the position about to be durable says that work is done:

```csharp
public async ValueTask OnCheckpoint(CheckpointContext context) {
    await _buffer.FlushAsync(context.CancellationToken);
    context.Logger.LogInformation("Committed at {Id}", context.CheckpointId);
}
```
