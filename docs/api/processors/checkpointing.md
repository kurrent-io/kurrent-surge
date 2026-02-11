---
order: 2
---

# Checkpointing and progress tracking

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

If the worker crashes before step 4 completes, no positions are lost. On restart, the processor resumes from the last successfully committed checkpoint and replays any records that were processed but not yet checkpointed. This provides at-least-once delivery guarantees.

## Configuring checkpoints

Checkpoint behavior is configured through `ConfigureCheckpointing` on the processor builder:

```csharp
await KurrentProcessors.Create(new MyProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .ConfigureCheckpointing(c => c
        .Interval(TimeSpan.FromSeconds(15))
        .Timeout(TimeSpan.FromSeconds(30))
        .RetentionCount(5)
        .FailurePolicy(CheckpointFailurePolicy.Continue))
    .RunAsync(stoppingToken);
```

### CheckpointSettings reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Interval` | `long` | `30000` | Time between automatic checkpoints, in milliseconds. Valid range: 1 - 3,600,000. |
| `Timeout` | `long` | `30000` | Maximum time to wait for workers to acknowledge a checkpoint, in milliseconds. Valid range: 1 - 300,000. |
| `RetentionCount` | `int` | `3` | Number of completed checkpoint records to retain in the checkpoint stream. Valid range: 1 - 100. |
| `FailurePolicy` | `CheckpointFailurePolicy` | `Continue` | What happens when a checkpoint fails. See [Failure policies](#failure-policies). |

### Failure policies

A checkpoint fails when a worker does not acknowledge within the timeout. The timed-out worker is always disconnected regardless of the policy. The `CheckpointFailurePolicy` controls what happens to the remaining workers:

| Value | Behavior |
|---|---|
| `Continue` | Abort the failed checkpoint and continue processing. The failed worker's partitions are reassigned to remaining workers. The next checkpoint triggers normally. Use when high availability is the priority. |
| `FailProcessor` | Stop all workers and put the processor into an idle state. Requires manual intervention to resume. Use when data consistency is critical and downtime is preferable to data loss. |

## Checkpoint triggers

Every checkpoint carries a `CheckpointTrigger` that identifies why it was created. This is available in the `CheckpointContext` passed to `OnCheckpoint` and recorded in `CheckpointProgress` for diagnostics.

| Trigger | Description |
|---|---|
| `Unspecified` | Default/fallback value. |
| `Rebalance` | Triggered during partition rebalancing to ensure a consistent state across workers before partitions are reassigned. |
| `Interval` | Triggered automatically by the server when the configured `Interval` elapses since the last checkpoint. This is the primary mechanism for regular checkpointing. Subject to retention count. |
| `CaughtUp` | Triggered exactly once per processor lifecycle when the subscription finishes processing historical data and transitions to live mode. Prevents replaying the entire history if the worker crashes shortly after going live. Subject to retention count. |
| `Savepoint` | Triggered by an explicit manual API call. Manual savepoints are not auto-deleted and are retained until explicitly removed. |
| `Shutdown` | Triggered during graceful worker shutdown to persist final state. This is a best-effort checkpoint. If it fails, positions replay on restart (at-least-once guarantee is preserved). |

## CheckpointContext

When a checkpoint is signaled, the SDK invokes `OnCheckpoint` with a `CheckpointContext`. This is your opportunity to flush any external state before the checkpoint is committed.

```csharp
public class OrderProcessor : IRecordProcessor {
    readonly OrderBatchWriter _writer;

    public OrderProcessor(OrderBatchWriter writer) {
        _writer = writer;
    }

    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records) {
            _writer.Buffer(record);
        }
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

`CheckpointContext` extends `WorkerContext`, inheriting identity and cancellation properties:

| Property | Type | Description |
|---|---|---|
| `CheckpointId` | `string` | Unique identifier for this checkpoint. A coordination token, not a position. |
| `Trigger` | `CheckpointTrigger` | What triggered the checkpoint (Interval, CaughtUp, Rebalance, Savepoint, Shutdown). |
| `Timestamp` | `DateTimeOffset` | When the checkpoint was signaled. |
| `ProcessorId` | `ProcessorId` | The processor group this worker belongs to. |
| `WorkerId` | `WorkerId` | This worker's unique identifier. |
| `SessionId` | `SessionId` | The current session identifier. |
| `Logger` | `ILogger` | Logger instance scoped to the worker. |
| `CancellationToken` | `CancellationToken` | Cancellation token for the worker's lifetime. During shutdown checkpoints, this is `CancellationToken.None` to allow the final flush to complete. |

## Using the delegate builder

If you do not need a full class, use `RecordProcessorBuilder` to wire up `OnCheckpoint` with a delegate:

```csharp
var processor = new RecordProcessorBuilder()
    .ProcessRecords(ctx => {
        foreach (var record in ctx.Records) {
            Console.WriteLine($"Processing: {record.Value}");
        }
    })
    .OnCheckpoint(ctx => {
        Console.WriteLine($"Checkpoint {ctx.CheckpointId} [{ctx.Trigger}]");
    });
```

Both synchronous (`Action<CheckpointContext>`) and asynchronous (`Func<CheckpointContext, ValueTask>`) overloads are available:

```csharp
var processor = new RecordProcessorBuilder()
    .ProcessRecordsAsync(async ctx => {
        await ProcessBatchAsync(ctx.Records, ctx.CancellationToken);
    })
    .OnCheckpointAsync(async ctx => {
        await FlushExternalStateAsync(ctx.CancellationToken);
    });
```

## Progress tracking

The `WorkerProgress` struct provides a comprehensive snapshot of the worker's processing state. It is available through `ProcessRecordsContext.Progress` during record processing and through `IProcessorWorker.Progress` at any time when you hold a reference to the worker.

```csharp
var worker = KurrentProcessors.Create(new MyProcessor())
    .ConnectTo("kurrentdb://localhost:2113")
    .WithProcessorId("order-processor")
    .Build();

// Start processing in the background
var processingTask = worker.RunAsync(stoppingToken);

// Monitor progress periodically
while (!stoppingToken.IsCancellationRequested) {
    var progress = worker.Progress;

    Console.WriteLine($"Records: {progress.Throughput.TotalRecords}");
    Console.WriteLine($"Rate: {progress.Throughput.RecordsPerSecond:F1}/s");
    Console.WriteLine($"Checkpoints: {progress.Checkpoint.TotalCheckpoints}");
    Console.WriteLine($"Partitions: {progress.PartitionCount}");

    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
}
```

### WorkerProgress

`WorkerProgress` is a read-only struct that aggregates throughput metrics, checkpoint status, and per-partition positions into a single snapshot.

| Property / Method | Type | Description |
|---|---|---|
| `Throughput` | `ThroughputMetrics` | Throughput and timing metrics. |
| `Checkpoint` | `CheckpointProgress` | Checkpoint progress information. |
| `Partitions` | `ImmutableDictionary<Partition, PartitionProgress>` | Per-partition position tracking. |
| `PartitionCount` | `int` | Number of partitions currently assigned. |
| `this[Partition]` | `PartitionProgress` | Gets progress for a specific partition. Throws `KeyNotFoundException` if not found. |
| `TryGetPartition(Partition, out PartitionProgress)` | `bool` | Safe lookup for partition progress. |
| `LowestCheckpointPartition` | `(Partition, PartitionProgress)?` | The partition with the lowest flushed position -- the one furthest behind. Returns `null` if no partitions are assigned. |

### ThroughputMetrics

Real-time throughput and timing information computed from the start of the processing session.

| Property | Type | Description |
|---|---|---|
| `TotalRecords` | `long` | Total number of records processed since the session started. |
| `TotalBatches` | `long` | Total number of batches processed. |
| `RecordsPerSecond` | `double` | Average records processed per second. |
| `BatchesPerSecond` | `double` | Average batches processed per second. |
| `AverageBatchSize` | `double` | Average number of records per batch. |
| `StartedAt` | `DateTimeOffset` | When processing started. |
| `Elapsed` | `TimeSpan` | Total elapsed time since processing started. |

### CheckpointProgress

Tracks checkpoint activity for the current session.

| Property | Type | Description |
|---|---|---|
| `TotalCheckpoints` | `long` | Total number of checkpoints completed. |
| `RecordsSinceLastCheckpoint` | `long` | Number of records processed since the last checkpoint committed. Useful for gauging how much work would be replayed on crash. |
| `LastTrigger` | `CheckpointTrigger` | What triggered the most recent checkpoint. |
| `LastCheckpointAt` | `DateTimeOffset?` | When the last checkpoint completed, or `null` if no checkpoints have occurred yet. |

### PartitionProgress

Tracks position progress for a single partition. The distinction between tracked and flushed positions is central to understanding at-least-once delivery:

- **Tracked** means the record was processed by your `ProcessRecords` handler.
- **Flushed** means the position was committed to the checkpoint store and will not be replayed.

| Property | Type | Description |
|---|---|---|
| `LastTrackedPosition` | `LogPosition` | The most recent position processed but not yet checkpointed. Returns `LogPosition.Unset` if no positions are pending. |
| `LastFlushedPosition` | `LogPosition` | The most recent position that has been checkpointed. Returns `LogPosition.Unset` if no checkpoints have occurred. |

The gap between `LastTrackedPosition` and `LastFlushedPosition` represents your "replay window" -- the records that would be redelivered if the worker crashed right now.

## Progress monitoring patterns

### Accessing progress during processing

Inside `ProcessRecords`, the current progress is available on the context:

```csharp
public async ValueTask ProcessRecords(ProcessRecordsContext context) {
    foreach (var record in context.Records) {
        // process the record
    }

    var progress = context.Progress;

    if (progress.Throughput.TotalRecords % 10000 == 0) {
        context.Logger.LogInformation(
            "Milestone: {Total} records at {Rate:F1}/s, {Checkpoints} checkpoints",
            progress.Throughput.TotalRecords,
            progress.Throughput.RecordsPerSecond,
            progress.Checkpoint.TotalCheckpoints);
    }
}
```

### Identifying the slowest partition

Use `LowestCheckpointPartition` to find the partition that is furthest behind:

```csharp
var progress = worker.Progress;
var slowest = progress.LowestCheckpointPartition;

if (slowest.HasValue) {
    Console.WriteLine(
        $"Slowest partition: {slowest.Value.Partition}, " +
        $"flushed at: {slowest.Value.Progress.LastFlushedPosition}");
}
```

### Monitoring the replay window

The gap between tracked and flushed positions tells you how much data would be replayed after a crash:

```csharp
var progress = worker.Progress;

foreach (var (partition, partitionProgress) in progress.Partitions) {
    var tracked = partitionProgress.LastTrackedPosition;
    var flushed = partitionProgress.LastFlushedPosition;

    if (tracked != flushed) {
        Console.WriteLine(
            $"Partition {partition}: replay window from {flushed} to {tracked}");
    }
}
```

### Reporting on completion or error

Both `CompleteContext` and `ErrorContext` include a final `WorkerProgress` snapshot:

```csharp
public class MonitoredProcessor : IRecordProcessor {
    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        // process records
    }

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

Checkpoints are stored in KurrentDB itself, in a dedicated stream per processor group. The stream is named `$processors/{processorId}/checkpoints`. Each checkpoint record contains the merged partition positions from all workers in the group.

The `RetentionCount` setting controls how many checkpoint records are kept in the stream. Older checkpoints are automatically removed through stream metadata configuration (`$maxCount`). This prevents the checkpoint stream from growing unbounded over time.

Multiple workers with the same `ProcessorId` share a single checkpoint stream. When a worker stores a checkpoint, it merges its partition positions with the existing state using optimistic concurrency. If two workers attempt to checkpoint simultaneously, the SDK retries automatically until the merge succeeds.
