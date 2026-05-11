# Client setup

<CopyMarkdownButton />

A single `KurrentClient` instance handles connections, retries, serialization, and routing for your entire application. Configure it through a connection string or a `KurrentClientOptions` record.

## Local development

For a node started with the [Quickstart](./quickstart.md) (Docker, no TLS), the client only needs the address and a flag to disable TLS.

::: tabs#connect-local

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");
```

@tab Options

```csharp
var options = new KurrentClientOptions {
    Endpoints = [new("localhost", 2113)],
    Security  = KurrentClientSecurityOptions.Insecure
};

await using var client = KurrentClient.Create(options);
```

:::

If your app uses dependency injection, skip to [Register with dependency injection](#register-with-dependency-injection).

## Connect with username and password

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

## Connect with mutual TLS

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

For combinations the factories don't cover (basic auth over a private CA, for example), compose transport and authentication directly:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = new() {
        Transport      = TransportSecurity.Certificate("/certs/ca.crt"),
        Authentication = ClientCredentials.Basic("admin", "changeit")
    }
};
```

## Connect to a cluster

For multi-node deployments, switch the scheme to `kurrentdb+discover` and list the seed nodes. The client uses gossip to discover the full cluster, then routes operations based on the configured preference.

::: tabs#cluster-discovery

@tab Connection string

```csharp
await using var client = KurrentClient.Create("kurrentdb+discover://admin:changeit@node1:2113,node2:2113,node3:2113?nodePreference=Follower");
```

@tab Options

```csharp
var options = new KurrentClientOptions {
    ConnectionScheme = KurrentConnectionScheme.Discover,
    Endpoints        = [
        new("node1", 2113),
        new("node2", 2113),
        new("node3", 2113)
    ],
    Security = KurrentClientSecurityOptions.Basic("admin", "changeit"),
    Gossip   = KurrentClientGossipOptions.Default with {
        ReadPreference = NodePreference.Follower
    }
};

await using var client = KurrentClient.Create(options);
```

:::

Available node preferences:

| Preference        | Description                          |
|-------------------|--------------------------------------|
| `Leader`          | Always route to the cluster leader   |
| `Follower`        | Prefer a follower for reads          |
| `ReadOnlyReplica` | Prefer a read-only replica           |
| `Random`          | Pick any reachable node (default)    |

For an mTLS cluster, combine certificate auth with discovery:

```csharp
await using var client = KurrentClient.Create("kurrentdb+discover://node1:2113,node2:2113,node3:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key&nodePreference=Follower");
```

## Register with dependency injection

`AddKurrentClient` registers a singleton client in `IServiceCollection`. One instance is thread-safe and reuses a single gRPC channel, so it serves the whole application.

::: tabs#di-overloads

@tab From appsettings.json

The parameterless overload reads `ConnectionStrings:KurrentDB` from `IConfiguration`:

```json
{
  "ConnectionStrings": {
    "KurrentDB": "kurrentdb://admin:changeit@kurrentdb.internal:2113"
  }
}
```

```csharp
services.AddKurrentClient();
```

If neither a connection string nor a `KurrentClientOptions` instance is registered, this overload throws at registration time.

@tab From a connection string

```csharp
services.AddKurrentClient("kurrentdb://admin:changeit@kurrentdb.internal:2113");
```

@tab From an options instance

```csharp
services.AddKurrentClient(new KurrentClientOptions {
    ConnectionName = "my-service",
    Endpoints      = [new("kurrentdb.internal", 2113)],
    Resilience     = KurrentClientResilienceOptions.HighAvailability
});
```

:::

Each overload accepts a configure callback as a final argument. The callback runs after options are resolved, so anything set there takes precedence:

```csharp
services.AddKurrentClient(opts => {
    opts.Resilience = KurrentClientResilienceOptions.HighAvailability;
});
```

All overloads use `TryAddSingleton`, so subsequent calls are ignored when a client is already registered.

Registration also pulls these services from the container and applies them to the options, overriding any value already set:

| Service             | Applied to                |
|---------------------|---------------------------|
| `MessageTypeMapper` | `options.Schema.Mapper`   |
| `ILoggerFactory`    | `options.LoggerFactory`   |
| `IMetadataDecoder`  | `options.MetadataDecoder` |

A registered `ILoggerFactory` wires logging into the client automatically. Outside DI, assign it on the options:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.LoggerFactory = loggerFactory
);
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
