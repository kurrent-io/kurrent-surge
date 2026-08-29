---
title: Clusters and discovery
description: How the client finds nodes, picks one, and follows a leader election.
---

A single-node connection has one address and no decisions to make. A cluster has several, and which
one a call should go to changes over time, because the leader changes. Discovery is how the client
answers that question continuously rather than once at startup.

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

## Following an election

An election is not an error, and the client does not surface it as one. When a call reaches a node
that is no longer the leader, the server says so, and the client learns the new leader from that
response rather than waiting for the next gossip round.

```
call ──▶ node1        "not leader, it is node2"
                            │
                            ▼
                    leader announced, routing updated
                            │
call ──▶ node2        succeeds
```

That path is why a leader change costs a retry rather than an outage, and why `nodePreference=leader`
is inexpensive to hold: the client is told when it is wrong instead of discovering it by timing out.

## Tuning discovery

The defaults suit a cluster on the same network as its clients. Change them when the path between
them is slower or less reliable than that.

| Parameter | Default | Raise it when |
|---|---|---|
| `maxDiscoverAttempts` | `10` | A single lost packet should not collapse discovery, such as over a VPN or between regions |
| `discoveryInterval` | `100` ms | A cluster is mid-election and backing off is better than hammering it |
| `gossipTimeout` | `5` s | The round trip to the cluster legitimately approaches five seconds |

Tune `gossipTimeout` against measured latency. Tune `keepAliveInterval` against the idle timeout of
whatever sits between the client and the cluster, which is a different question with a different
answer. See [Connection strings](/connect/connection-strings/) for both.

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
