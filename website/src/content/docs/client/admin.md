---
title: Admin
description: Control the connected node, and run scavenges.
---

`client.Admin` controls the connected node and runs scavenges. These are tools for orchestration
and maintenance scripts. Application code rarely calls them.

Every operation needs an authenticated connection with sufficient privileges.


## Shut down a node

```csharp
await client.Admin.ShutdownServer();
```

Returns once the server acknowledges. The node finishes its in-flight work and exits. Operations
against it fail until it restarts.

In a cluster, shutting down the leader triggers an election. The remaining nodes keep serving.

## Move leadership

`ResignNode` forces the current leader to step down and starts a new election. Use it before
maintenance, or to test failover.

```csharp
await client.Admin.ResignNode();
```

It only acts on the leader. A follower accepts the request and does nothing.

`SetNodePriority` biases the next election toward higher-priority nodes. It does not move leadership
by itself, so pair the two to hand off deliberately:

```csharp
await client.Admin.SetNodePriority(100);
await client.Admin.ResignNode();
```

`nodePriority` must be non-negative. A negative value throws `ArgumentOutOfRangeException`.

## Run a scavenge

Scavenging reclaims disk by removing soft-deleted records and merging chunk files. It runs per node
and you start it explicitly.

```csharp
var response = await client.Admin.StartScavenge();

var tuned = await client.Admin.StartScavenge(threadCount: 4, startFromChunk: 100);
```

| Parameter | Default | Constraint |
|---|---|---|
| `threadCount` | `1` | positive |
| `startFromChunk` | `0` | non-negative |

**Returns `ScavengeResponse`:** `ScavengeId` and `Status`, where `Status` is `Started`,
`AlreadyRunning`, or `StopRequested`.

This is the server's acknowledgement, not the outcome of the run. When a scavenge is already running,
the server returns `AlreadyRunning` with the existing id rather than starting a second one.

## Stop a scavenge

```csharp
await client.Admin.StopScavenge(scavengeId);
await client.Admin.StopCurrentScavenge();
```

`StopScavenge` requests cancellation; the run moves from `Running` to `Stopped` once the worker
notices. `StopCurrentScavenge` skips tracking the id, and throws `InvalidOperationException` when
nothing is running.

## Inspect scavenges

`ListScavenges` streams summaries newest first. It reads the `$scavenges` stream, so it yields
nothing when no scavenge has ever run.

```csharp
await foreach (var summary in client.Admin.ListScavenges())
    Console.WriteLine("{0,-12} {1,-10} {2}", summary.ScavengeId, summary.State, summary.StartedAt);
```

**`ScavengeSummary`:** `ScavengeId`, `NodeEndpoint`, `State`, `StartedAt`, `CompletedAt`, `Duration`,
`SpaceSaved`.

`GetScavengeDetails` reads the per-scavenge stream and folds its chunk, merge, and index events into
one snapshot.

```csharp
var details = await client.Admin.GetScavengeDetails(scavengeId);

Console.WriteLine("{0} chunks scavenged, {1} merged, {2} bytes saved",
    details.Progress.ChunksScavenged,
    details.Progress.ChunksMerged,
    details.Progress.SpaceSaved);
```

**`ScavengeDetails`** adds to the summary's fields:

| Member | Type | Meaning |
|---|---|---|
| `Error` | `string` | Set when `State` is `Errored` |
| `Configuration` | `ScavengeConfiguration` | `Threads`, `StartFromChunk`, `MergeChunks` |
| `Progress` | `ScavengeProgress` | Chunk and index counters |

`Duration` is computed live for a running scavenge. `CompletedAt` stays `default` until it finishes.

### States

| `ScavengeState` | Meaning |
|---|---|
| `Running` | In progress |
| `Completed` | Finished. Serialises as `Success` on the wire |
| `Stopped` | Stopped before completion |
| `Errored` | Failed. Read `Error` |
| `Interrupted` | Cut short by a node restart |
