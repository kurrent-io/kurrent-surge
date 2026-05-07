---
order: 2
---

# Scavenging

Scavenging reclaims disk space by removing soft-deleted events and merging chunk files. It runs per-node and is initiated explicitly. The `client.Admin` module starts, stops, and inspects scavenge operations.

```csharp
using Kurrent.Client.Admin;

var response = await client.Admin.StartScavenge();
Console.WriteLine("Scavenge {0}: {1}", response.ScavengeId, response.Status);
```

Scavenge operations are tracked in the `$scavenges` system stream and per-scavenge detail streams (`$scavenges-{id}`). The methods on `AdminClient` project those streams into typed records.

## Start a scavenge

```csharp
// Default: 1 thread, start from chunk 0
var response = await client.Admin.StartScavenge();

// Tune for a large database
var response = await client.Admin.StartScavenge(
    threadCount: 4,
    startFromChunk: 100
);
```

| Parameter | Type | Default | Description |
|---|---|---|---|
| `threadCount` | `int` | `1` | Number of worker threads. Must be positive. |
| `startFromChunk` | `int` | `0` | Chunk number to start from. Must be non-negative. |

The response is the server's acknowledgment, not the outcome of the run:

| Field | Type | Description |
|---|---|---|
| `ScavengeId` | `string` | Identifier for the scavenge run. |
| `Status` | `ScavengeResponseStatus` | `Started`, `AlreadyRunning`, or `StopRequested`. |

If a scavenge is already running, the server returns `AlreadyRunning` and the existing scavenge id rather than starting a new one.

## Stop a scavenge

```csharp
await client.Admin.StopScavenge(response.ScavengeId);
```

`StopScavenge` requests cancellation. The scavenge transitions through `Running → Stopped` once the worker observes the request. The returned `ScavengeResponse.Status` is `StopRequested` when a stop was issued, or returned without error when the scavenge had already finished.

To stop the currently running scavenge without tracking its id:

```csharp
await client.Admin.StopCurrentScavenge();
```

Throws `InvalidOperationException` if no scavenge is running.

## Inspect the latest scavenge

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

| Property | Type |
|---|---|
| `ScavengeId` | `string` |
| `NodeEndpoint` | `string` |
| `State` | `ScavengeState` |
| `StartedAt` | `DateTime` |
| `CompletedAt` | `DateTime` |
| `Duration` | `TimeSpan` |
| `SpaceSaved` | `long` |

## List scavenges

`ListScavenges` streams summaries from newest to oldest:

```csharp
await foreach (var summary in client.Admin.ListScavenges()) {
    Console.WriteLine("{0,-12} {1,-10} {2}", summary.ScavengeId, summary.State, summary.StartedAt);
}
```

Backed by a read of the `$scavenges` stream from end to start. Returns no items if the stream does not exist (no scavenge has ever run).

## Detailed progress

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

| Property | Type | Description |
|---|---|---|
| `ScavengeId` | `string` | Identifier. |
| `NodeEndpoint` | `string` | Node that ran the scavenge. |
| `State` | `ScavengeState` | Current outcome. |
| `StartedAt` | `DateTime` | Start timestamp. |
| `CompletedAt` | `DateTime` | Completion timestamp (`default` while running). |
| `Duration` | `TimeSpan` | For running scavenges, computed live as `UtcNow - StartedAt`. |
| `Error` | `string` | Error message when `State == Errored`. |
| `Configuration` | `ScavengeConfiguration` | `Threads`, `StartFromChunk`, `MergeChunks`. |
| `Progress` | `ScavengeProgress` | Aggregated chunk and index counters. |

## States

| `ScavengeState` | Meaning |
|---|---|
| `Running` | Scavenge is in progress. |
| `Completed` | Finished successfully. Serialized as `Success` on the wire. |
| `Stopped` | Manually stopped before completion. |
| `Errored` | Failed; check `ScavengeDetails.Error`. |
| `Interrupted` | Cut short by a node restart. |
