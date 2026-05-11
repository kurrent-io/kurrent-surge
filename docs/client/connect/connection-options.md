---
order: 3
---

# Connection Options

<CopyMarkdownButton />

Connection options control how the client authenticates, secures its transport, and recovers from failures. They apply on top of any [connection target](./connection-targets.md) and can be set in the connection string or on `KurrentClientOptions`.

## Username and password

The default `kurrentdb` scheme enables TLS automatically. The client refuses plaintext when TLS is on.

::: tabs#auth-basic

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113");
```

To trust a custom certificate authority, supply the CA file path:

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113?tlsCaFile=/certs/ca.crt");
```

@tab Options

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = KurrentClientSecurityOptions.Basic("admin", "changeit")
};

await using var client = KurrentClient.Create(options);
```

:::

## Mutual TLS

For certificate-based authentication, point the client at a client certificate and key.

::: tabs#auth-mtls

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://kurrentdb.internal:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key");
```

@tab Options

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = KurrentClientSecurityOptions.MutualTls(
        clientCertPath: "/certs/client.crt",
        clientKeyPath:  "/certs/client.key",
        rootCaPath:     "/certs/ca.crt")
};

await using var client = KurrentClient.Create(options);
```

:::

For combinations the factories do not cover (basic auth over a private CA, for example), compose transport and authentication directly:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = new() {
        Transport      = TransportSecurity.Certificate("/certs/ca.crt"),
        Authentication = ClientCredentials.Basic("admin", "changeit")
    }
};
```

For an mTLS cluster, combine certificate auth with discovery:

```csharp
await using var client = KurrentClient.Create("kurrentdb+discover://node1:2113,node2:2113,node3:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key&nodePreference=Follower");
```

## Tune resilience

The client ships predefined profiles for common scenarios:

| Profile            | Retries     | Deadline    | Use for                                          |
|--------------------|-------------|-------------|--------------------------------------------------|
| `Default`          | 3           | infinite    | General-purpose use                              |
| `NoResilience`     | 0           | infinite    | Disable retries entirely                         |
| `FailFast`         | 0           | 5s          | Latency-sensitive operations                     |
| `HighAvailability` | 5           | 30s         | Production reads, 200ms fixed backoff            |
| `CautiousWrite`    | 3           | 60s         | Conservative for non-idempotent writes           |
| `Subscription`     | 8           | infinite    | Long-running subscriptions                       |
| `RetryForever`     | unlimited   | infinite    | Retry forever with exponential backoff           |

Apply a profile when building the client:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@kurrentdb.internal:2113",
    opts => opts.Resilience = KurrentClientResilienceOptions.HighAvailability);
```

Override individual settings on top of a profile:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@kurrentdb.internal:2113",
    opts => opts.Resilience = KurrentClientResilienceOptions.HighAvailability with {
        Deadline          = TimeSpan.FromSeconds(10),
        KeepAliveInterval = TimeSpan.FromSeconds(30),
        KeepAliveTimeout  = TimeSpan.FromSeconds(15)
    });
```

The two-argument `Create` overload parses the connection string into options first, then runs the callback, so anything set in the callback wins.

## Connection string reference

These parameters apply to every connection string, whether you target a local node, a single production server, or a cluster. Names are case-insensitive.

| Parameter             | Type   | Default  | Description                                                                     |
|-----------------------|--------|----------|---------------------------------------------------------------------------------|
| `tls`                 | bool   | `true`   | Enable TLS transport security                                                   |
| `tlsVerifyCert`       | bool   | `true`   | Verify the server TLS certificate                                               |
| `tlsCaFile`           | string | _none_   | Path to a custom CA certificate file                                            |
| `userCertFile`        | string | _none_   | Path to client certificate (mTLS)                                               |
| `userKeyFile`         | string | _none_   | Path to client private key (mTLS)                                               |
| `connectionName`      | string | auto     | Connection identifier for diagnostics                                           |
| `defaultDeadline`     | int    | `30000`  | Client-side timeout in milliseconds (`-1` = infinite)                           |
| `keepAliveInterval`   | int    | `60000`  | Keep-alive ping interval in milliseconds (`-1` = infinite)                      |
| `keepAliveTimeout`    | int    | `30000`  | Keep-alive response timeout in milliseconds (`-1` = infinite)                   |
| `maxDiscoverAttempts` | int    | `10`     | Maximum gossip discovery attempts                                               |
| `discoveryInterval`   | int    | `100`    | Milliseconds between discovery attempts                                         |
| `gossipTimeout`       | int    | `5000`   | Individual discovery attempt timeout in milliseconds                            |
| `nodePreference`      | enum   | `Random` | Node selection for reads (`Leader` / `Follower` / `ReadOnlyReplica` / `Random`) |

Supported schemes: `kurrentdb` and `kurrentdb+discover`.
