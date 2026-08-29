---
title: Load balancing
description: The client routes through a real gRPC load balancing policy, not a wrapper that picks an address at startup.
---

Node selection in Kurrent Surge is a gRPC load balancing policy, registered on the channel and
running inside it. The channel resolves addresses, ranks them, holds the connection, queues calls
while it reconnects, and follows a leader election, because those are things a gRPC balancer does.
Nothing above the channel has to know.

Most clients wrap the channel instead: resolve an address, construct a `GrpcChannel` for it, and
rebuild the whole thing when that address stops working. This one is a level lower.

## What is registered

```
GrpcChannel.ForAddress("kurrent:///<connection-name>")
        │
        ├─ ServiceConfig.LoadBalancingConfigs  ──▶  "kurrent-node-preference"
        │
        ├─ ServiceProvider ─┬─ ResolverFactory      ──▶  GossipResolver
        │                   └─ LoadBalancerFactory  ──▶  KurrentDBLoadBalancer
        │
        └─ Interceptors ────── LeaderChangeInterceptor
```

| Piece | Type it implements | What it does |
|---|---|---|
| `GossipResolver` | `PollingResolver` | Reads gossip and publishes the members, ranked |
| `KurrentDBLoadBalancer` | `LoadBalancer` | Owns the subchannel and decides when to move it |
| Four pickers | `SubchannelPicker` | Answer each call: connected, connecting, reconnect, failed |
| `LeaderChangeInterceptor` | `Interceptor` | Turns a not-leader response into a routing change |

The address is `kurrent:///<connection-name>`, a scheme the resolver factory claims. The balancer and
the resolver are registered in a service provider private to that one channel, so two clients in the
same process never share resolver state.

## How an address becomes a connection

The resolver publishes the full membership every pass, ordered by your `nodePreference`, with the
rank attached to each address. Rank 0 is the routing target. The rest are not idle: they are the
fallback list the transport already holds.

```
gossip ──▶ rank the members ──▶ publish [rank0, rank1, rank2]
                                        │
                                        ▼
                              one subchannel, given all three
                                        │
                       rank0 unreachable, transport tries rank1
```

The balancer keeps one subchannel at a time, pointed at rank 0 and carrying the whole ranked list.
When rank 0 changes, because the leader moved or the preferred node went away, the subchannel is
replaced so traffic follows the new target instead of staying on a connection that still works but
is no longer the one you asked for.

That is a deliberate shape, not a limitation of the policy. Calls are not spread across nodes,
because a client that writes wants the leader, and one that reads from followers says so with
`nodePreference` and gets a follower. Spreading writes would only add redirects.

## What a call sees while the connection moves

Each state has a picker, and the picker is what a call actually hits.

| Channel state | Picker | A call is |
|---|---|---|
| Ready | `ConnectedPicker` | Sent |
| Connecting | `QueuePicker` | Queued, not failed |
| Idle | `RequestConnectionPicker` | Sent, and reconnection is triggered |
| TransientFailure | `FailurePicker` | Failed with the underlying status |

Queuing is the part worth knowing. A call issued while the balancer is replacing a subchannel waits
for the new one rather than throwing, so a leader election is not something your code has to catch.

A resolver failure while the channel is connected is logged and ignored. Gossip being briefly
unreachable is not a reason to drop a working connection.

## Following an election without a gossip round trip

When a write reaches a node that is no longer the leader, the server answers `FailedPrecondition`
and names the new one. The interceptor hands that endpoint to the resolver, which republishes
immediately with the announced leader at rank 0 and the previously published members behind it.

```
call ──▶ node1     FailedPrecondition, leader is node2
                          │
                          ▼
              AnnounceLeader(node2), republish
                          │
call ──▶ node2     succeeds
```

No gossip query happens on that path. Discovery resumes on the next scheduled refresh, so an
announcement is a shortcut rather than a replacement for gossip.

## Configuring it

The policy has no settings of its own. It runs on what discovery gives it, so everything you tune
is on the connection string.

| Parameter | Default | Effect on routing |
|---|---|---|
| `nodePreference` | `leader` | The ranking the resolver publishes |
| `maxDiscoverAttempts` | `10` | How long discovery tries before failing the channel |
| `discoveryInterval` | `100` ms | The wait between failed discovery rounds |

See [Clusters and discovery](/client/clusters-and-discovery/) for choosing between them, and
[Connection strings](/client/connection-strings/) for the full parameter list.

Retries live in the same `ServiceConfig` as the balancing policy, as a gRPC method config, so a
retry is enforced by the transport rather than by a loop above it. See
[Configuring the client](/client/configuring-the-client/).

## Direct connections use the same path

A single-node `kurrentdb://` connection runs the identical policy with discovery switched off. The
resolver publishes the seed verbatim, and the balancer, the pickers and the leader interceptor all
behave the same. A direct connection still follows a leader redirect for that reason.
