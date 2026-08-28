---
title: Admin
description: Scavenging, node priority and shutdown.
---

Node administration. Operational rather than application-facing.

```csharp
ValueTask<ScavengeId>       StartScavenge(...)
ValueTask                   StopScavenge(...)
ValueTask<ScavengeDetails>  GetScavengeDetails(...)
ValueTask<IReadOnlyList<…>> ListScavenges(...)
ValueTask                   ResignNode(...)
ValueTask                   SetNodePriority(...)
ValueTask                   ShutdownServer(...)
```

## Scavenging

Scavenging reclaims disk from deleted and expired records. It is I/O heavy and runs for a long time
on a large database, so start it deliberately rather than on a schedule you have not measured.

```csharp
var scavengeId = await client.Admin.StartScavenge();
var details    = await client.Admin.GetScavengeDetails(scavengeId);
```

`StopScavenge` halts a run; progress already made is kept.

## Cluster control

`ResignNode` makes the current leader step down and triggers an election — the graceful way to move
leadership before maintenance.

`SetNodePriority` influences which node is preferred in a future election.

## Shutdown

`ShutdownServer` stops the node. There is no undo through the client — the node has to be restarted
by whatever supervises it.

:::danger
These operate on the server, not your application. `ShutdownServer` and `ResignNode` change cluster
state for every client connected to it, not only yours.
:::
