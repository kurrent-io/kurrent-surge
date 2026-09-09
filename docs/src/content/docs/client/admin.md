---
title: Admin
description: Control the connected node from the client.
---

`client.Admin` controls the connected node. These are tools for orchestration and maintenance
scripts. Application code rarely calls them.

Every operation needs an authenticated connection with sufficient privileges.

| Area | What it covers |
|---|---|
| Node control | Shutting a node down, and moving leadership between nodes |
| [Scavenging](/kurrent-surge/client/admin/scavenging/) | Reclaiming disk: starting, stopping and inspecting scavenges |

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

## Scavenging

Reclaiming disk is its own surface. See
[Scavenging](/kurrent-surge/client/admin/scavenging/).
