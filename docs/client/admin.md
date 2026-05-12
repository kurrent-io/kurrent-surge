# Admin operations

<CopyMarkdownButton />

The `Admin` module exposes operator-level controls against a KurrentDB cluster. It covers two areas: cluster operations (graceful shutdown, leader resignation, node priority) and scavenging (reclaiming disk space by removing soft-deleted records and merging chunk files). These are tools for orchestration and maintenance scripts, not application code.

All operations are asynchronous and require an authenticated connection with sufficient privileges. They throw on failure. See [Error handling](./error-handling.md) for the exception catalogue.

## Cluster operations

Cluster operations target the node the client is connected to. In a clustered deployment they may indirectly affect leadership.

### Shutdown a node

Initiates a graceful shutdown of the connected node.

```csharp
await client.Admin.ShutdownServer();
```

The call returns once the server has acknowledged the request. The node finishes in-flight work and exits the process. Subsequent operations against the same node fail until it restarts.

In a cluster, shutting down the leader triggers a leader election. The remaining nodes continue serving traffic.

### Resign the leader

Forces the current leader to step down and triggers a new election. Useful for moving leadership off a node before maintenance, or for testing failover behavior.

```csharp
await client.Admin.ResignNode();
```

The call only has effect when issued against the current leader. On a follower, the server accepts the request but does nothing.

### Set node priority

Node priority influences leader election. Higher priority nodes are preferred when multiple candidates are eligible. Setting priority does not immediately move leadership. It takes effect at the next election.

```csharp
await client.Admin.SetNodePriority(10);
```

| Parameter      | Type  | Description                                                                                                              |
|----------------|-------|--------------------------------------------------------------------------------------------------------------------------|
| `nodePriority` | `int` | Non-negative integer. Higher values are preferred at election time. Negative values throw `ArgumentOutOfRangeException`. |

To force the current leader to hand off to a higher-priority node, combine `SetNodePriority` with `ResignNode`:

```csharp
await client.Admin.SetNodePriority(100);
await client.Admin.ResignNode();
```

The new election favors the node with the highest priority.

## Scavenging

Scavenging runs per-node and is initiated explicitly. Each run is tracked in the `$scavenges` system stream and a per-scavenge detail stream. The methods below project those streams into typed records.

### Start a scavenge

```csharp
// Default: 1 thread, start from chunk 0
var response = await client.Admin.StartScavenge();
Console.WriteLine("Scavenge {0}: {1}", response.ScavengeId, response.Status);
```

```csharp
// Tune for a large database
var response = await client.Admin.StartScavenge(
    threadCount: 4,
    startFromChunk: 100
);
```

| Parameter        | Type  | Default | Description                                            |
|------------------|-------|---------|--------------------------------------------------------|
| `threadCount`    | `int` | `1`     | Number of worker threads. Must be positive.            |
| `startFromChunk` | `int` | `0`     | Chunk number to start from. Must be non-negative.      |

The response is the server's acknowledgment, not the outcome of the run:

| Field        | Type                     | Description                                          |
|--------------|--------------------------|------------------------------------------------------|
| `ScavengeId` | `string`                 | Identifier for the scavenge run.                     |
| `Status`     | `ScavengeResponseStatus` | `Started`, `AlreadyRunning`, or `StopRequested`.     |

If a scavenge is already running, the server returns `AlreadyRunning` along with the existing scavenge id rather than starting a new one.

### Stop a scavenge

```csharp
await client.Admin.StopScavenge(scavengeId);
```

`StopScavenge` requests cancellation. The scavenge transitions from `Running` to `Stopped` once the worker observes the request. The returned status is `StopRequested` when a stop was issued, or returned without error when the scavenge had already finished.

To stop the currently running scavenge without tracking its id:

```csharp
await client.Admin.StopCurrentScavenge();
```

Throws `InvalidOperationException` if no scavenge is running.

### Inspect the latest scavenge

```csharp
var summary = await client.Admin.GetLatestScavenge();

if (summary is not null) {
    Console.WriteLine("{0}: {1} (saved {2} bytes)",
        summary.ScavengeId,
        summary.State,
        summary.SpaceSaved);
}
```

Returns `null` when no scavenge has ever been started.

`ScavengeSummary` is a lightweight projection covering the most common fields:

| Property       | Type            |
|----------------|-----------------|
| `ScavengeId`   | `string`        |
| `NodeEndpoint` | `string`        |
| `State`        | `ScavengeState` |
| `StartedAt`    | `DateTime`      |
| `CompletedAt`  | `DateTime`      |
| `Duration`     | `TimeSpan`      |
| `SpaceSaved`   | `long`          |

### List scavenges

`ListScavenges` streams summaries from newest to oldest:

```csharp
await foreach (var summary in client.Admin.ListScavenges()) {
    Console.WriteLine("{0,-12} {1,-10} {2}", summary.ScavengeId, summary.State, summary.StartedAt);
}
```

Backed by a read of the `$scavenges` stream from end to start. Returns no items if the stream does not exist (no scavenge has ever run).

### Detailed progress

`GetScavengeDetails` reads the per-scavenge detail stream and folds chunk, merge, and index events into a progress snapshot:

```csharp
var details = await client.Admin.GetScavengeDetails(scavengeId);

Console.WriteLine("State:      {0}", details.State);
Console.WriteLine("Duration:   {0}", details.Duration);
Console.WriteLine("Threads:    {0}", details.Configuration.Threads);
Console.WriteLine("Chunks:     {0} scavenged, {1} skipped, {2} merged",
    details.Progress.ChunksScavenged,
    details.Progress.ChunksSkipped,
    details.Progress.ChunksMerged);
Console.WriteLine("Index:      {0} tables scavenged, {1} entries deleted",
    details.Progress.IndexTablesScavenged,
    details.Progress.IndexEntriesDeleted);
Console.WriteLine("Saved:      {0} bytes", details.Progress.SpaceSaved);
```

`ScavengeDetails` exposes:

| Property        | Type                     | Description                                                  |
|-----------------|--------------------------|--------------------------------------------------------------|
| `ScavengeId`    | `string`                 | Identifier.                                                  |
| `NodeEndpoint`  | `string`                 | Node that ran the scavenge.                                  |
| `State`         | `ScavengeState`          | Current outcome.                                             |
| `StartedAt`     | `DateTime`               | Start timestamp.                                             |
| `CompletedAt`   | `DateTime`               | Completion timestamp (`default` while running).              |
| `Duration`      | `TimeSpan`               | For running scavenges, computed live as `UtcNow - StartedAt`.|
| `Error`         | `string`                 | Error message when `State == Errored`.                       |
| `Configuration` | `ScavengeConfiguration`  | `Threads`, `StartFromChunk`, `MergeChunks`.                  |
| `Progress`      | `ScavengeProgress`       | Aggregated chunk and index counters.                         |

### States

| `ScavengeState` | Meaning                                                    |
|-----------------|------------------------------------------------------------|
| `Running`       | Scavenge is in progress.                                   |
| `Completed`     | Finished successfully. Serialized as `Success` on the wire.|
| `Stopped`       | Manually stopped before completion.                        |
| `Errored`       | Failed. Check `ScavengeDetails.Error`.                     |
| `Interrupted`   | Cut short by a node restart.                               |
