---
title: Connect
description: Reach a node or a cluster, and configure how the client talks to it.
---

Connecting is one line. Everything else on these pages is about the cases where the default is not
what you want: a cluster, a private CA, a network that closes idle connections.

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");
```

| Page | What it covers |
|---|---|
| [Connection strings](/connect/connection-strings/) | Every scheme and parameter, with its unit and default |
| [Configuring the client](/connect/configuring-the-client/) | Options, resilience and security profiles, dependency injection |
| [Clusters and discovery](/connect/clusters-and-discovery/) | Gossip, node preference, and following an election |
| [Security](/connect/security/) | TLS, private CAs, passwords and client certificates |
| [Troubleshooting](/connect/troubleshooting/) | Working from a connection symptom to its cause |

## One client per process

A client owns a channel per node and multiplexes every call over it. Build one at startup, keep it
for the life of the process, and dispose it with `await using`.

Constructing one per request pays a TCP handshake, a TLS handshake and, against a cluster, a gossip
round trip, on every request. The client is safe to share.

## Single node or cluster

The scheme decides. A direct scheme addresses exactly one node and does not fail over. A discovery
scheme takes seeds, learns the membership from gossip, and follows it.

```
kurrentdb://localhost:2113                       one node
kurrentdb+discover://node1:2113,node2:2113       a cluster
```

Use a discovery scheme for any cluster, including a single-node one you expect to grow.

## The defaults

Worth knowing before you change anything.

| | Default |
|---|---|
| Transport | TLS on, certificate verified |
| Node preference | `leader` |
| Resilience | 3 retries, no call deadline |
| Keepalive | ping every 10 s, 10 s timeout |

Every one of them is reachable from the connection string, and from `KurrentClientOptions` in code.
