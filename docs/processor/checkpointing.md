---
order: 2
---

# Checkpointing and progress tracking

<CopyMarkdownButton />

Checkpointing is the mechanism that makes processor execution resumable. When a processor restarts after a crash, deployment, or scaling event, it resumes from the last committed checkpoint rather than reprocessing every record from the beginning. Without checkpointing, every restart would mean replaying the entire history of your event log.

The SDK handles checkpointing automatically. Your processor receives a callback before each checkpoint, giving you an opportunity to flush external state (write to a database, send a batch of messages, etc.) before the checkpoint is committed. Progress tracking gives you real-time visibility into throughput, checkpoint status, and per-partition positions.

## How checkpointing works

The checkpoint lifecycle follows a coordinated sequence:

1. The server signals a checkpoint based on a trigger (timer interval, caught-up, rebalance, shutdown, or manual savepoint).
2. The SDK snapshots all pending partition positions.
3. Your `OnCheckpoint` callback is invoked, giving you a chance to flush external state.
4. The SDK persists the positions to the checkpoint store in KurrentDB.
5. The SDK acknowledges the checkpoint back to the server.
6. Internal position trackers are advanced past the flushed positions.

If the drone crashes before step 4 completes, no positions are lost. On restart, the processor resumes from the last successfully committed checkpoint and replays any records that were processed but not yet checkpointed. This provides at-least-once delivery guarantees.

## Configuring checkpoints

Checkpoint behavior is configured through `ProcessorOptions.Checkpoint` directly, or through the chained `opts.Checkpointing(...)` API:

```csharp
await KurrentProcessor.Create(new MyProcessor(), opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId = "order-processor";
        opts.Checkpointing(c => c
            .Interval(TimeSpan.FromSeconds(15))
            .RetentionCount(5)
            .FailurePolicy(CheckpointFailurePolicy.Continue));
    })
    .RunAsync(stoppingToken);
```

### CheckpointOptions reference

| Property         | Type                       | Default     | Description                                                                                |
|------------------|----------------------------|-------------|--------------------------------------------------------------------------------------------|
| `Interval`       | `TimeSpan`                 | 30 seconds  | Time between automatic checkpoints. In JSON config, accepts `"00:00:30"` or `30000` (ms).  |
| `RetentionCount` | `int`                      | `3`         | Number of completed checkpoint records to retain. Range: 1–100.                            |
| `FailurePolicy`  | `CheckpointFailurePolicy`  | `Continue`  | What happens when a checkpoint fails. See [Failure policies](#failure-policies).            |

### Failure policies

A checkpoint fails when a drone does not acknowledge within the server-side timeout. The timed-out drone is always disconnected. The `CheckpointFailurePolicy` controls what happens to the remaining drones:

| Value      | Behavior                                                                                                                                                          |
|------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Continue` | Abort the failed checkpoint and continue processing. The failed drone's partitions are reassigned to remaining drones. The next checkpoint triggers normally. Use when high availability is the priority. |
| `Fail`     | Stop all drones and put the processor into an idle state. Requires manual intervention to resume. Use when data consistency is critical and downtime is preferable to data loss. |

## Checkpoint triggers

Every checkpoint carries a `CheckpointTrigger` that identifies why it was created. This is available in the `CheckpointContext` passed to `OnCheckpoint` and recorded in `CheckpointProgress` for diagnostics.

| Trigger       | Description                                                                                                              |
|---------------|--------------------------------------------------------------------------------------------------------------------------|
| `Unspecified` | Default/fallback value.                                                                                                  |
| `Rebalance`   | Triggered during partition rebalancing to ensure consistent state across drones before partitions are reassigned.        |
| `Interval`    | Triggered automatically when the configured `Interval` elapses since the last checkpoint. Subject to retention count.    |
| `CaughtUp`    | Triggered exactly once per processor lifecycle when the subscription transitions from historical to live mode. Subject to retention count. |
| `Savepoint`   | Triggered by an explicit manual API call. Manual savepoints are not auto-deleted and are retained until explicitly removed. |
| `Shutdown`    | Triggered during graceful shutdown to persist final state. Best-effort. If it fails, positions replay on restart.        |

## CheckpointContext

When a checkpoint is signaled, the SDK invokes `OnCheckpoint` with a `CheckpointContext`. This is your opportunity to flush any external state before the checkpoint is committed.

```csharp
public class OrderProcessor : IRecordProcessor {
    readonly OrderBatchWriter _writer;

    public OrderProcessor(OrderBatchWriter writer) {
        _writer = writer;
    }

    public ValueTask ProcessRecord(ProcessRecordContext context) {
        _writer.Buffer(context.Record);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
        // Flush buffered writes before the checkpoint is committed.
        // If this fails, the checkpoint is not committed and records
        // will be redelivered on restart.
        await _writer.FlushAsync(context.CancellationToken);

        context.Logger.LogInformation(
            "Checkpoint {CheckpointId} triggered by {Trigger} at {Timestamp}",
            context.CheckpointId,
            context.Trigger,
            context.Timestamp);
    }
}
```

### CheckpointContext properties

`CheckpointContext` extends `DroneContext`, inheriting identity and cancellation properties:

| Property            | Type                  | Description                                                                                                            |
|---------------------|-----------------------|------------------------------------------------------------------------------------------------------------------------|
| `CheckpointId`      | `string`              | Unique identifier for this checkpoint. A coordination token, not a position.                                           |
| `Trigger`           | `CheckpointTrigger`   | What triggered the checkpoint (Interval, CaughtUp, Rebalance, Savepoint, Shutdown).                                    |
| `Timestamp`         | `DateTimeOffset`      | When the checkpoint was signaled.                                                                                      |
| `Positions`         | `PartitionPositions`  | Partition positions captured at signal time.                                                                           |
| `ProcessorId`       | `ProcessorId`         | The processor group this drone belongs to.                                                                             |
| `DroneId`           | `string`              | This drone's unique identifier.                                                                                        |
| `SessionId`         | `SessionId`           | The current session identifier.                                                                                        |
| `Logger`            | `ILogger`             | Logger instance scoped to the drone.                                                                                   |
| `CancellationToken` | `CancellationToken`   | Cancellation token for the drone's lifetime. During shutdown checkpoints, this is `CancellationToken.None` so the final flush can complete. |

## Using MessageProcessor

If you do not need a full class, register `OnCheckpoint` directly on a `MessageProcessor`:

```csharp
await KurrentProcessor.Create(handlers => handlers
        .Process<OrderPlaced>((msg, ctx) => Console.WriteLine($"Placed: {msg.OrderId}"))
        .OnCheckpoint(ctx => {
            Console.WriteLine($"Checkpoint {ctx.CheckpointId} [{ctx.Trigger}]");
        }),
    opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.ProcessorId = "order-processor";
    })
    .RunAsync(stoppingToken);
```

Both synchronous (`Action<CheckpointContext>`) and asynchronous (`Func<CheckpointContext, ValueTask>`) overloads are available:

```csharp
.OnCheckpoint(async ctx => {
    await FlushExternalStateAsync(ctx.CancellationToken);
})
```

## Progress tracking

The `DroneProgress` struct provides a comprehensive snapshot of the drone's processing state. It is available through `ProcessRecordContext.Progress` during record processing, and on `ErrorContext` / `CompleteContext` at termination.

```csharp
public ValueTask ProcessRecord(ProcessRecordContext context) {
    var progress = context.Progress;

    if (progress.Throughput.TotalRecords % 10000 == 0) {
        context.Logger.LogInformation(
            "Milestone: {Total} records at {Rate:F1}/s, {Checkpoints} checkpoints",
            progress.Throughput.TotalRecords,
            progress.Throughput.RecordsPerSecond,
            progress.Checkpoint.TotalCheckpoints);
    }

    return ValueTask.CompletedTask;
}
```

### DroneProgress

`DroneProgress` is a read-only struct that aggregates throughput metrics, checkpoint status, and per-partition positions into a single snapshot.

| Property / Method                                  | Type                                                  | Description                                                                              |
|----------------------------------------------------|-------------------------------------------------------|------------------------------------------------------------------------------------------|
| `Throughput`                                       | `ThroughputMetrics`                                   | Throughput and timing metrics.                                                           |
| `Checkpoint`                                       | `CheckpointProgress`                                  | Checkpoint progress information.                                                         |
| `Partitions`                                       | `ImmutableDictionary<Partition, PartitionProgress>`   | Per-partition position tracking.                                                         |
| `PartitionCount`                                   | `int`                                                 | Number of partitions currently assigned.                                                 |
| `this[Partition]`                                  | `PartitionProgress`                                   | Gets progress for a specific partition. Throws `KeyNotFoundException` if not found.      |
| `TryGetPartition(Partition, out PartitionProgress)`| `bool`                                                | Safe lookup for partition progress.                                                      |
| `LowestCheckpointPartition`                        | `(Partition, PartitionProgress)?`                     | The partition with the lowest flushed position (the one furthest behind).                |

### ThroughputMetrics

Real-time throughput and timing information computed from the start of the processing session.

| Property           | Type             | Description                                              |
|--------------------|------------------|----------------------------------------------------------|
| `TotalRecords`     | `long`           | Total number of records processed since the session started. |
| `RecordsPerSecond` | `double`         | Average records processed per second.                    |
| `StartedAt`        | `DateTimeOffset` | When processing started.                                 |
| `Elapsed`          | `TimeSpan`       | Total elapsed time since processing started.             |

### CheckpointProgress

Tracks checkpoint activity for the current session.

| Property                     | Type                | Description                                                                                            |
|------------------------------|---------------------|--------------------------------------------------------------------------------------------------------|
| `TotalCheckpoints`           | `long`              | Total number of checkpoints completed.                                                                 |
| `RecordsSinceLastCheckpoint` | `long`              | Records processed since the last checkpoint committed. Useful for gauging how much work would be replayed on crash. |
| `LastTrigger`                | `CheckpointTrigger` | What triggered the most recent checkpoint.                                                             |
| `LastCheckpointAt`           | `DateTimeOffset?`   | When the last checkpoint completed, or `null` if no checkpoints have occurred yet.                     |

### PartitionProgress

Tracks position progress for a single partition. The distinction between tracked and flushed positions is central to understanding at-least-once delivery:

- **Tracked** means the record was processed by your `ProcessRecord` handler.
- **Flushed** means the position was committed to the checkpoint store and will not be replayed.

| Property              | Type             | Description                                                                                            |
|-----------------------|------------------|--------------------------------------------------------------------------------------------------------|
| `LastTrackedPosition` | `RecordPosition` | The most recent position processed but not yet checkpointed. Returns `RecordPosition.Unset` if none.   |
| `LastFlushedPosition` | `RecordPosition` | The most recent position that has been checkpointed. Returns `RecordPosition.Unset` if no checkpoints. |

The gap between `LastTrackedPosition` and `LastFlushedPosition` represents your "replay window": the records that would be redelivered if the drone crashed right now.

## Progress monitoring patterns

### Identifying the slowest partition

Use `LowestCheckpointPartition` to find the partition that is furthest behind:

```csharp
public ValueTask OnCheckpoint(CheckpointContext context) {
    // Snapshot progress wherever you have access to it (e.g. another lifecycle hook).
    return ValueTask.CompletedTask;
}

public ValueTask ProcessRecord(ProcessRecordContext context) {
    var slowest = context.Progress.LowestCheckpointPartition;
    if (slowest.HasValue) {
        context.Logger.LogDebug(
            "Slowest partition: {Partition}, flushed at: {Position}",
            slowest.Value.Partition,
            slowest.Value.Item2.LastFlushedPosition);
    }
    return ValueTask.CompletedTask;
}
```

### Monitoring the replay window

The gap between tracked and flushed positions tells you how much data would be replayed after a crash:

```csharp
public ValueTask OnCheckpoint(CheckpointContext context) {
    foreach (var (partition, partitionProgress) in /* obtain DroneProgress */) {
        var tracked = partitionProgress.LastTrackedPosition;
        var flushed = partitionProgress.LastFlushedPosition;

        if (tracked != flushed) {
            context.Logger.LogDebug(
                "Partition {Partition}: replay window from {Flushed} to {Tracked}",
                partition, flushed, tracked);
        }
    }
    return ValueTask.CompletedTask;
}
```

### Reporting on completion or error

Both `CompleteContext` and `ErrorContext` include a final `DroneProgress` snapshot:

```csharp
public class MonitoredProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) =>
        ValueTask.CompletedTask;

    public ValueTask OnComplete(CompleteContext context) {
        var t = context.Progress.Throughput;

        context.Logger.LogInformation(
            "Completed: {Total} records in {Elapsed}, avg {Rate:F1}/s",
            t.TotalRecords,
            t.Elapsed,
            t.RecordsPerSecond);

        return ValueTask.CompletedTask;
    }

    public ValueTask OnError(ErrorContext context) {
        var t = context.Progress.Throughput;
        var c = context.Progress.Checkpoint;

        context.Logger.LogError(context.Error,
            "Failed after {Total} records, {Pending} since last checkpoint",
            t.TotalRecords,
            c.RecordsSinceLastCheckpoint);

        return ValueTask.CompletedTask;
    }
}
```

## Checkpoint storage

Checkpoints are stored in KurrentDB itself, in a dedicated stream per processor group. The stream is named `$processors/{processorId}/checkpoints`. Each checkpoint record contains the merged partition positions from all drones in the group.

The `RetentionCount` setting controls how many checkpoint records are kept in the stream. Older checkpoints are automatically removed through stream metadata configuration (`$maxCount`). This prevents the checkpoint stream from growing unbounded over time.

Multiple drones with the same `ProcessorId` share a single checkpoint stream. When a drone stores a checkpoint, it merges its partition positions with the existing state using optimistic concurrency. If two drones attempt to checkpoint simultaneously, the SDK retries automatically until the merge succeeds.
