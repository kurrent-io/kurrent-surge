---
title: Breaking Changes
description: Behaviour that changed in the client, and what each change means for existing code.
---

Changes that alter what existing code does at runtime. Each entry states the old behaviour, the new
behaviour, and what to do about it.

## Connection string durations are read in seconds

`keepAliveInterval`, `keepAliveTimeout` and `gossipTimeout` were read as milliseconds. They are now
read as seconds, which is what every other KurrentDB SDK does and what the client already wrote when
it generated a connection string.

```
?keepAliveInterval=10&keepAliveTimeout=10&gossipTimeout=5

before ..... 10 ms ping interval, 10 ms ping timeout, 5 ms gossip timeout
after ...... 10 s  ping interval, 10 s  ping timeout, 5 s  gossip timeout
```

`discoveryInterval` and `defaultDeadline` are unchanged. Both remain milliseconds, again matching the
other SDKs.

**What to do:** a connection string written for the v1 .NET client, or copied from the KurrentDB
documentation, now means what it says. A string tuned against the old millisecond behaviour needs its
three values divided by 1000.

| Parameter | Unit |
|---|---|
| `keepAliveInterval` | seconds |
| `keepAliveTimeout` | seconds |
| `gossipTimeout` | seconds |
| `discoveryInterval` | milliseconds |
| `defaultDeadline` | milliseconds |

A value of `-1` means no limit, and carries no unit.

## Durations accept an explicit unit

A duration may now carry an `ms` or `s` suffix, which overrides the unit in the table above.

```
?keepAliveInterval=500ms&gossipTimeout=3s&discoveryInterval=2s
```

The suffix is specific to this client. A connection string carrying one does not parse in the Java,
Go, Rust, Node.js or Python clients, so use the bare form for anything shared between SDKs.

## The client validates a connection string

`KurrentClientOptions.Parse` now validates and throws. A negative or zero duration used to be
accepted and reached the live connection.

```csharp
KurrentClientOptions.Parse("kurrentdb://localhost?keepAliveInterval=-500");   // now throws
KurrentClientOptions.Parse("kurrentdb://localhost?keepAliveInterval=0");      // now throws
```

Building `KurrentClientOptions` in code is unaffected. Validation there is still explicit, through
`EnsureValid()`.

## Node preference defaults to the leader

`nodePreference` defaulted to `random`. It now defaults to `leader`, matching every other SDK.

A client that writes should always target the leader, so this removes a redirect on the write path.
A read-only client that wants reads spread across the cluster must now say so:

```
?nodePreference=follower
```

## The client is resilient by default

Client resilience defaulted to the `NoResilience` profile, which disables retries. It now defaults to
the `Default` profile: three attempts with exponential backoff, starting at 250 ms.

Parsing a connection string used to replace the whole profile, which turned retries on regardless of
the declared default. Parsing now overrides only the parameters the connection string carries, so the
profile survives.

**What to do:** an application that relies on a failure surfacing immediately should select the
profile explicitly.

```csharp
opts.Resilience = KurrentClientResilienceOptions.NoResilience;
```

## Keepalive defaults are 10 seconds

The ping interval was 60 seconds and the ping timeout 30 seconds. Both are now 10 seconds, matching
the other SDKs.

Set them explicitly when a network path needs different pacing. Keep the interval at or below half
the idle timeout of any NAT gateway, proxy or load balancer between the client and the cluster.

## The call deadline applies, and defaults to infinite

`defaultDeadline` was parsed and stored but never applied to a call. It now applies. The `FailFast`,
`HighAvailability` and `CautiousWrite` profiles carry a deadline, so selecting one of those now has
the effect it always described.

The default is infinite, so a client that sets no deadline is unaffected.

:::caution
A deadline covers a whole call. On a subscription, which is a long-running server stream, the
deadline bounds the stream and terminates it when it fires. Leave the deadline infinite on a client
that subscribes, and set a deadline per call instead.
:::

## Generating a connection string

`ToString()` now writes durations with an explicit unit suffix. Pass `legacy: true` for the bare form
the other SDKs read.

```csharp
options.ToConnectionString();              // keepAliveInterval=10s
options.ToConnectionString(legacy: true);  // keepAliveInterval=10
```

The legacy form carries whole units only, so a sub-second value rounds down.
