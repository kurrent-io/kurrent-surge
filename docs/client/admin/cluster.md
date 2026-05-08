---
order: 1
---

# Cluster operations

<CopyMarkdownButton />

The `client.Admin` module exposes node-level operations against a KurrentDB cluster: graceful shutdown, leader resignation, and node priority changes. These are operator tools, intended for orchestration and maintenance scripts.

```csharp
var admin = client.Admin;
```

All operations are asynchronous and require an authenticated connection with sufficient privileges. They throw on failure. See [Error handling](../error-handling.md) for the exception catalogue.

## Shutdown a node

Initiates a graceful shutdown of the node the client is connected to.

```csharp
await client.Admin.ShutdownServer();
```

The call returns once the server has acknowledged the request. The node finishes in-flight work and exits the process. Subsequent operations against the same node will fail until the node restarts.

In a clustered deployment, shutting down the leader triggers a leader election. The other nodes continue serving traffic.

## Resign the leader

Forces the current leader to step down and triggers a new election. Useful for moving leadership off a node before maintenance, or for testing failover behavior.

```csharp
await client.Admin.ResignNode();
```

The call only has effect when issued against the current leader. On a follower, the server accepts the request but does nothing.

## Set node priority

Node priority influences leader election. Higher priority nodes are preferred when multiple candidates are eligible. Setting priority does not immediately move leadership; it takes effect at the next election.

```csharp
await client.Admin.SetNodePriority(10);
```

| Parameter | Type | Description |
|---|---|---|
| `nodePriority` | `int` | Non-negative integer. Higher values are preferred at election time. Must be `>= 0` (negative values throw `ArgumentOutOfRangeException`). |

To force the current leader to hand off to a higher-priority node, combine `SetNodePriority` with `ResignNode`:

```csharp
await client.Admin.SetNodePriority(100);
await client.Admin.ResignNode();
```

The new election will favor the node with the highest priority.
