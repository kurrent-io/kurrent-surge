---
title: Clusters and Load Balancing
description: How the client discovers nodes, ranks them, holds a connection, and follows a leader election.
---

A single-node connection has one address and no decisions to make. A cluster has several, and which
one a call should go to changes over time, because the leader changes. Discovery is how the client
answers that question continuously rather than once at startup.

Discovery answers that question a level lower than most clients do. Node selection here is a gRPC
load balancing policy registered on the channel. The channel itself resolves addresses, ranks them,
holds the connection, queues calls while it reconnects, and follows an election. Most clients wrap
the channel instead:
resolve an address, construct a `GrpcChannel` for it, and rebuild the whole thing when that address
stops working.

## Why discovery rather than a fixed address

Pinning a client to `kurrentdb://node1:2113` addresses that node and only that node. When node1 stops
being the leader, writes still go there and pay a redirect. When node1 goes away, the client goes
with it, even though the other two members are healthy.

```
kurrentdb://node1:2113                          one node, no failover
kurrentdb+discover://node1:2113,node2:2113      the cluster, followed as it changes
```

The hosts in a discovery string are seeds, not the membership. The client asks one of them for the
cluster's gossip, and from then on it works from what gossip reports. Listing every member makes the
first contact more likely to succeed; it does not pin anything.

## How a node gets chosen

Gossip returns the members and their roles. The client ranks them by your `nodePreference` and uses
the best one available.

| Preference | Ranks toward |
|---|---|
| `leader` | The leader. The default, and correct for any client that writes |
| `follower` | A follower, keeping read load off the leader |
| `readOnlyReplica` | A read-only replica, so reads never touch a voting member |
| `random` | Any available member |

Ranking is a preference, not a filter. When no node of the preferred role is available the client
still connects, because a degraded connection is more useful than none.

## What runs inside the channel

```d2 layout=elk
direction: down

C: GrpcChannel
SC: ServiceConfig (LoadBalancingConfigs)
POL: kurrent-node-preference
SP: ServiceProvider
RF: ResolverFactory
GR: GossipResolver
LF: LoadBalancerFactory
LB: KurrentDBLoadBalancer
INT: Interceptors
LCI: LeaderChangeInterceptor

C -> SC -> POL
C -> SP
SP -> RF -> GR
SP -> LF -> LB
C -> INT -> LCI
```

| Piece | Type it implements | What it does |
|---|---|---|
| `GossipResolver` | `PollingResolver` | Reads gossip and publishes the members, ranked |
| `KurrentDBLoadBalancer` | `LoadBalancer` | Owns the subchannel and decides when to move it |
| Four pickers | `SubchannelPicker` | Answer each call: connected, connecting, reconnect, failed |
| `LeaderChangeInterceptor` | `Interceptor` | Turns a not-leader response into a routing change |

The channel address is `kurrent:///<connection-name>`, a scheme the resolver factory claims. The
balancer and the resolver live in a service provider private to that one channel, so two clients in
the same process never share resolver state.

## How an address becomes a connection

The resolver publishes the full membership every pass, ordered by your `nodePreference`, with the rank
attached to each address. Rank 0 is the routing target. The rest are not idle: they are the fallback
list the transport already holds.

```d2 width=290
direction: down

G: gossip
R: rank the members
P: "publish rank 0, 1, 2"
S: one subchannel, whole list
F: transport tries rank 1

G -> R -> P -> S
S -> F: rank 0 unreachable
```

The balancer keeps one subchannel at a time, pointed at rank 0 and carrying the whole ranked list.
When rank 0 changes, because the leader moved or the preferred node went away, the balancer replaces
the subchannel. Traffic then follows the new target instead of staying on a connection that still
works but is no longer the one you asked for.

One subchannel is a deliberate shape, not a limit of the policy. Calls are not spread across nodes,
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

A call issued while the balancer is replacing a subchannel waits for the new one rather than
throwing, so a leader election is not something your code has to catch.

A resolver failure while the channel is connected is logged and ignored. Gossip being briefly
unreachable is not a reason to drop a working connection.

## Following an election

An election is not an error, and the client does not surface it as one. When a call reaches a node
that is no longer the leader, the server answers `FailedPrecondition` and names the new one. The
interceptor hands that endpoint to the resolver, which republishes immediately with the announced
leader at rank 0 and the previously published members behind it.

```d2
shape: sequence_diagram

C: Your call
I: LeaderChangeInterceptor
R: GossipResolver
N1: node1
N2: node2

C -> N1.attempt: append
N1.attempt -> I.handle: FailedPrecondition, leader is node2
I.handle -> R.republish: AnnounceLeader(node2)
R.republish.note: rank 0 becomes node2
C -> N2.retry: append
N2.retry -> C: succeeds
```

No gossip query happens on that path. That is why a leader change costs a retry rather than an
outage, and why `nodePreference=leader` is inexpensive to hold. The client is told when it is wrong
instead of discovering it by timing out. Discovery resumes on the next scheduled refresh, so an
announcement is a shortcut rather than a replacement for gossip.

## Tuning discovery

The policy has no settings of its own. It runs on what discovery gives it, so everything you tune is
on the connection string. The defaults suit a cluster on the same network as its clients. Change them
when the path between them is slower or less reliable than that.

| Parameter | Default | Raise it when |
|---|---|---|
| `maxDiscoverAttempts` | `10` | A single lost packet should not collapse discovery, such as over a VPN or between regions |
| `discoveryInterval` | `100` ms | A cluster is mid-election and backing off is better than hammering it |
| `gossipTimeout` | `5` s | The round trip to the cluster approaches five seconds |

Tune `gossipTimeout` against measured latency. Tune `keepAliveInterval` against the idle timeout of
whatever sits between the client and the cluster, which is a different question with a different
answer. See the [Connection String Reference](/kurrent-surge/client/connection-strings/) for both.

Retries live in the same `ServiceConfig` as the balancing policy, as a gRPC method config, so a retry
is enforced by the transport rather than by a loop above it. See
[Configuring the Client](/kurrent-surge/client/configuring-the-client/).

## Reads and writes on different nodes

Node preference belongs to the client, not to the call. An application that writes to the leader and
reads from followers builds two clients, each with its own connection string.

```csharp
// Writes
"kurrentdb+discover://cluster:2113?nodePreference=leader"

// Reads
"kurrentdb+discover://cluster:2113?nodePreference=follower"
```

They share nothing internally, so the second client costs its own channels and nothing else. Reading
from a follower means reading data that may lag the leader slightly, which is the trade you are
making in exchange for keeping read load off it.

## Direct connections use the same path

A single-node `kurrentdb://` connection runs the identical policy with discovery switched off. The
resolver publishes the seed verbatim, and the balancer, the pickers and the leader interceptor all
behave the same. A direct connection still follows a leader redirect for that reason.
