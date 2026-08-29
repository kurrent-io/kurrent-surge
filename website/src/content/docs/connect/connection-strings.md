---
title: Connection strings
description: Every scheme and parameter the client accepts, with its unit and default.
---

A connection string names the nodes to reach and configures how the client talks to them. The format
is shared across every KurrentDB SDK, so a string written for one client means the same thing here.

```
kurrentdb+discover://admin:changeit@node1:2113,node2:2113?tls=true&nodePreference=leader
└── scheme ──────┘   └ credentials ┘ └───── hosts ──────┘ └────── parameters ───────┘
```

## Schemes

Six schemes parse. The `esdb` and `eventstore` forms are aliases kept for connection strings written
against the older clients.

| Scheme | Topology |
|---|---|
| `kurrentdb://` | A single node, addressed directly |
| `kurrentdb+discover://` | A cluster, discovered through gossip |
| `esdb://` | Alias for `kurrentdb://` |
| `esdb+discover://` | Alias for `kurrentdb+discover://` |
| `eventstore://` | Alias for `kurrentdb://` |
| `eventstore+discover://` | Alias for `kurrentdb+discover://` |

A string carrying more than one host must use a discovery scheme. Several hosts under a direct scheme
is a configuration error, because a direct connection addresses exactly one node.

## Credentials

Username and password go in the userinfo part of the URI, before the host. Percent-encode any
reserved character.

```
kurrentdb://admin:changeit@localhost:2113
kurrentdb://user%40domain:p%40ssw%3Ard@localhost:2113
```

Certificate credentials come from `userCertFile` and `userKeyFile` instead. When both are set they
take precedence over a username and password.

## Parameters

### Transport security

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `tls` | boolean | `true` | Whether to connect over TLS. Set `false` only for an insecure local node |
| `tlsVerifyCert` | boolean | `true` | Whether to verify the server certificate. `false` silences certificate errors, including a genuine one |
| `tlsCaFile` | path | none | CA bundle to verify against, when the cluster certificate is signed by a private CA |
| `userCertFile` | path | none | Client certificate for certificate authentication |
| `userKeyFile` | path | none | Private key for `userCertFile` |
| `userKeyPassword` | string | none | Password protecting `userKeyFile` |

### Discovery

Applies to the `+discover` schemes.

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `nodePreference` | enum | `leader` | Which node to prefer. `leader`, `follower`, `random`, or `readOnlyReplica` |
| `maxDiscoverAttempts` | number | `10` | How many times to attempt discovery before failing |
| `discoveryInterval` | milliseconds | `100` | Wait between discovery attempts |
| `gossipTimeout` | seconds | `5` | Deadline for a single gossip request |

`readPreference` is accepted as a synonym for `nodePreference`, and wins if a string somehow carries
both.

### Connection health

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `keepAliveInterval` | seconds | `10` | Wait between keepalive pings on an idle connection |
| `keepAliveTimeout` | seconds | `10` | Deadline for a keepalive ping before the connection is considered dead |
| `defaultDeadline` | milliseconds | none | Deadline applied to a call that sets none of its own |

### Identification

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `connectionName` | string | generated | Label that appears in server-side connection metrics |

## Duration units

The unit is fixed per parameter, and it is the unit the other KurrentDB SDKs use. It is not uniform
across parameters.

| Unit | Parameters |
|---|---|
| seconds | `keepAliveInterval`, `keepAliveTimeout`, `gossipTimeout` |
| milliseconds | `discoveryInterval`, `defaultDeadline` |

A value of `-1` means no limit, and carries no unit.

```
?keepAliveInterval=10      10 seconds
?gossipTimeout=5           5 seconds
?discoveryInterval=100     100 milliseconds
?defaultDeadline=-1        no deadline
```

### Explicit units

A duration may carry an `ms` or `s` suffix, which overrides the unit above.

```
?keepAliveInterval=500ms&discoveryInterval=2s
```

The suffix is specific to this client. A string carrying one does not parse in the Java, Go, Rust,
Node.js or Python clients, so keep to the bare form for anything shared between SDKs.

## Validation

`KurrentClientOptions.Parse` validates and throws. A duration that is negative, other than `-1`, or
zero is rejected rather than reaching a live connection. `keepAliveTimeout` must not be greater than
`keepAliveInterval`.

## Generating a connection string

`ToConnectionString` renders options back into a string. Durations carry an explicit unit by default,
which round-trips exactly. Pass `legacy: true` for the bare form the other clients read.

```csharp
options.ToConnectionString();              // keepAliveInterval=10s
options.ToConnectionString(legacy: true);  // keepAliveInterval=10
```

The legacy form carries whole units only, so a sub-second value rounds down. `ToString()` returns the
default form.

Parameters left at their default are omitted from the output.
