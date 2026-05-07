---
order: 2
---

# Client setup

<CopyMarkdownButton />

`KurrentClient` is the entry point to KurrentDB. A single instance handles connections, retries, serialization, and routing for your entire application, and is configured through a `KurrentClientOptions` record.

## Connect to a local development node

For local development with KurrentDB running in Docker (as in the [Quickstart](./quickstart.md)), the client only needs the address and a flag to disable TLS. You can supply this through a connection string or build the options programmatically. If your app uses dependency injection, skip ahead to [Register with dependency injection](#register-with-dependency-injection).

::: tabs#connect-local

@tab Connection string

The connection string is the most concise option. The `?tls=false` query disables TLS for plaintext local development.

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");
```

@tab Options

Build a `KurrentClientOptions` record when you want to set fields directly without going through a string. `KurrentClientSecurityOptions.Insecure` disables TLS without you having to remember the query parameter.

```csharp
var options = new KurrentClientOptions {
    Endpoints = [new("localhost", 2113)],
    Security  = KurrentClientSecurityOptions.Insecure
};

await using var client = KurrentClient.Create(options);
```

:::

`KurrentClient.Create` parses the connection string, configures a gRPC channel, and applies sensible defaults for resilience and serialization.

## Authenticate with basic credentials

The default scheme `kurrentdb` enables TLS automatically, and the client refuses to connect to a plaintext endpoint when TLS is on.

::: tabs#auth-basic

@tab Connection string

Embed the username and password in the connection string.

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113");
```

To trust a custom certificate authority, supply the CA file path:

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@kurrentdb.internal:2113?tlsCaFile=/certs/ca.crt");
```

@tab Options

Use the `KurrentClientSecurityOptions.Basic` factory to configure transport and credentials in one call.

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = KurrentClientSecurityOptions.Basic("admin", "changeit")
};

await using var client = KurrentClient.Create(options);
```

:::

## Authenticate with mutual TLS

If your cluster uses certificate-based authentication, point the client at the client certificate and key.

::: tabs#auth-mtls

@tab Connection string

Pass the certificate and key paths as query parameters. TLS is on by default with the `kurrentdb` scheme.

```csharp
await using var client = KurrentClient.Create("kurrentdb://kurrentdb.internal:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key");
```

@tab Options

Use the `KurrentClientSecurityOptions.MutualTls` factory to configure both transport and client authentication in one call. A custom CA can be supplied through `rootCaPath`.

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

If neither factory fits your setup (for example, basic auth over a private CA), compose `TransportSecurity` and `ClientCredentials` directly:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://kurrentdb.internal:2113") with {
    Security = new() {
        Transport      = TransportSecurity.Certificate("/certs/ca.crt"),
        Authentication = ClientCredentials.Basic("admin", "changeit")
    }
};
```

## Discover cluster nodes

For clustered deployments, switch the scheme to `kurrentdb+discover` and list the seed nodes. The client uses gossip to discover the full cluster and routes operations based on `nodePreference`.

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

| Preference        | Description                          |
|-------------------|--------------------------------------|
| `Leader`          | Always route to the cluster leader   |
| `Follower`        | Prefer a follower for reads          |
| `ReadOnlyReplica` | Prefer a read-only replica           |
| `Random`          | Pick any reachable node (default)    |

For an mTLS cluster, combine the patterns above:

```csharp
await using var client = KurrentClient.Create("kurrentdb+discover://node1:2113,node2:2113,node3:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key&nodePreference=Follower");
```

## Register with dependency injection

`AddKurrentClient` registers `KurrentClient` as a singleton in `IServiceCollection`. One instance is thread-safe and reuses a single gRPC channel, so it serves the whole application. Each overload suits a different configuration source.

::: tabs#di-overloads

@tab From appsettings.json

The parameterless overload reads the connection string from `ConnectionStrings:KurrentDB` in `IConfiguration`. Add the entry to your `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "KurrentDB": "kurrentdb://admin:changeit@kurrentdb.internal:2113"
  }
}
```

Then register the client with no arguments:

```csharp
services.AddKurrentClient();
```

If neither a connection string nor a `KurrentClientOptions` instance is registered, this overload throws at registration time.

@tab From a connection string

Pass the connection string directly when you don't need it in configuration:

```csharp
services.AddKurrentClient("kurrentdb://admin:changeit@kurrentdb.internal:2113");
```

@tab From an options instance

Construct a `KurrentClientOptions` record for full programmatic control:

```csharp
services.AddKurrentClient(new KurrentClientOptions {
    ConnectionName = "my-service",
    Endpoints      = [new("kurrentdb.internal", 2113)],
    Resilience     = KurrentClientResilienceOptions.HighAvailability
});
```

:::

Each overload also accepts a configure callback as a final argument. The callback runs after options are resolved from the service provider and configuration, so anything you set here overrides earlier values:

```csharp
services.AddKurrentClient(opts => {
    opts.Resilience = KurrentClientResilienceOptions.HighAvailability;
});
```

All overloads use `TryAddSingleton`, so subsequent calls are ignored when a client is already registered.

### Service-provider overrides

Whichever overload you choose, the registration applies these services to the options before constructing the client. Each one overrides any value already set on the options.

| Service             | Applied to                |
|---------------------|---------------------------|
| `MessageTypeMapper` | `options.Schema.Mapper`   |
| `ILoggerFactory`    | `options.LoggerFactory`   |
| `IMetadataDecoder`  | `options.MetadataDecoder` |

Registering `ILoggerFactory` in DI is enough to wire it into the client. No separate call is needed.

### Configure logging outside DI

When constructing a client without a service provider, assign `LoggerFactory` directly:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.LoggerFactory = loggerFactory
);
```

## Tune resilience

The client ships with predefined resilience profiles for common scenarios:

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

Tune individual settings on top of a profile with `with`:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@kurrentdb.internal:2113",
    opts => opts.Resilience = KurrentClientResilienceOptions.HighAvailability with {
        Deadline          = TimeSpan.FromSeconds(10),
        KeepAliveInterval = TimeSpan.FromSeconds(30),
        KeepAliveTimeout  = TimeSpan.FromSeconds(15)
    });
```

`KurrentClient.Create(connectionString, configure)` parses the string into options first and then runs your callback, so anything you set in the callback wins.

## Connection string reference

The parameters below apply to every connection string, regardless of whether you are connecting to a local node, a single production server, or a cluster. Parameter names are case-insensitive.

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

Supported schemes are `kurrentdb` and `kurrentdb+discover`.
