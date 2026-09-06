---
title: Configuring the client
description: Build a client, pick a resilience and security profile, and register it for injection.
---

A connection string covers most configuration. `KurrentClientOptions` covers the rest, and everything
a connection string sets is reachable there too.

## Build a client

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113");

await using var client = KurrentClient.Create("kurrentdb://localhost:2113", opts => {
    opts.Resilience = KurrentClientResilienceOptions.Subscription;
    opts.LoggerFactory = loggerFactory;
});

var options = KurrentClientOptions.Parse("kurrentdb://localhost:2113");
await using var client = KurrentClient.Create(options);
```

The client is `IAsyncDisposable`, so dispose it with `await using`.

Build one client per process and keep it. It holds a channel per node and multiplexes every call over
them, so constructing one per request pays a TCP handshake, a TLS handshake and, on a cluster, a
gossip round trip that it did not need to.

## What the options carry

| Member | Purpose |
|---|---|
| `ConnectionName` | Label that appears in server-side connection metrics |
| `ConnectionScheme` | `Direct` or `Discover` |
| `Endpoints` | The nodes to reach |
| `Gossip` | Discovery and node preference |
| `Security` | TLS and credentials |
| `Resilience` | Retries, keepalive, call deadline |
| `Schema` | Registration and validation |
| `Messages` | Message catalogs and codecs |
| `Compression` | Wire compression |
| `Initialization` | Work that runs before the first operation |
| `Interceptors` | gRPC interceptors added to the channel |
| `LoggerFactory` | Where the client logs |
| `MetadataDecoder` | How record metadata is decoded |

Parsing a connection string overrides only the parameters that string carries. Everything else keeps
the value from the profile it started on.

## Resilience profiles

Seven profiles. Each sets a call deadline and a retry policy; keepalive stays at the default of 10
seconds unless you change it.

| Profile | Deadline | Retries | For |
|---|---|---|---|
| `Default` | infinite | 3, 250 ms initial, 10 s max, 1.5x | General use. The default |
| `NoResilience` | infinite | disabled | Relying on server behaviour or an external retry layer |
| `FailFast` | 5 s | disabled | An immediate answer, typically UI-facing |
| `RetryForever` | infinite | unlimited, 100 ms initial, 30 s max, 2.0x | Critical idempotent work that must eventually land |
| `Subscription` | infinite | 8, 100 ms initial, 15 s max, 2.0x | Long-lived subscriptions |
| `HighAvailability` | 30 s | 5, fixed 200 ms interval | Predictable recovery inside a fixed window |
| `CautiousWrite` | 60 s | 3, 500 ms initial, 15 s max, 1.5x | Writes where idempotency is not guaranteed |

```csharp
opts.Resilience = KurrentClientResilienceOptions.Subscription;

opts.Resilience = KurrentClientResilienceOptions.Default;
opts.Resilience.KeepAliveInterval = TimeSpan.FromSeconds(20);
```

:::caution
A deadline covers a whole call, and a subscription is one long call. `FailFast`, `HighAvailability`
and `CautiousWrite` all carry a finite deadline, so a subscription running under them is terminated
when it fires. Use `Subscription` or `Default` on a client that subscribes, and set a deadline per
call on the operations that need one.
:::

## Security profiles

| Profile | Effect |
|---|---|
| `Tls` | TLS on, certificate verified. The default |
| `Insecure` | TLS off. Local development only |
| `Password(username, password)` | TLS with basic credentials |
| `MutualTls(certPath, keyPath, rootCaPath?, password?)` | Client certificate authentication from files |
| `MutualTls(certificate, rootCaCertificate?)` | Client certificate authentication from loaded certificates |

```csharp
opts.Security = KurrentClientSecurityOptions.Password("admin", "changeit");

opts.Security = KurrentClientSecurityOptions.MutualTls(
    clientCertPath: "/certs/client.crt",
    clientKeyPath:  "/certs/client.key",
    rootCaPath:     "/certs/ca.crt");
```

## Dependency injection

`AddKurrentSurge` is the single entry point, and it registers a `KurrentClient` for injection.

```csharp
builder.Services.AddKurrentSurge(surge => surge
    .Connection("kurrentdb://admin:changeit@localhost:2113?tls=false"));
```

A client resolved when nothing configured a connection throws
`KurrentClientNotConfiguredException` rather than connecting to a default.

[Dependency injection](/kurrent-surge/client/dependency-injection/) covers the three entry points,
the connection precedence and configuration binding.

## Two clients for two node preferences

Node preference is bound to the client. An application that writes to the leader and reads from
followers builds two clients rather than sharing one.

```csharp
var writes = KurrentClient.Create("kurrentdb+discover://cluster:2113?nodePreference=leader");
var reads  = KurrentClient.Create("kurrentdb+discover://cluster:2113?nodePreference=follower");
```

They share nothing internally, so there is no cost beyond the second set of channels. The
registration scope registers at most one client, so hand the second one over explicitly.
