---
order: 2
---

# Configuration

`KurrentClient` is configured through a single `KurrentClientOptions` record. You can build it from a connection string, mutate it programmatically, or resolve it from dependency injection. Choose the path that fits your application.

## Connection strings

The connection string is the simplest way to configure the client.

### Single node (direct connection)

```
kurrentdb://admin:changeit@localhost:2113?tls=true
```

### Cluster with discovery (gossip)

```
kurrentdb+discover://admin:changeit@node1:2113,node2:2113,node3:2113
```

The `kurrentdb+discover` scheme enables gossip-based node discovery for clustered deployments.

### Supported schemes

| Scheme               | Description                                    |
|----------------------|------------------------------------------------|
| `kurrentdb`          | Direct connection to a single node             |
| `kurrentdb+discover` | Cluster connection with gossip discovery       |
| `esdb`               | Legacy alias for `kurrentdb`                   |
| `esdb+discover`      | Legacy alias for `kurrentdb+discover`          |

### Connection string parameters

| Parameter             | Type   | Default  | Description                                                                 |
|-----------------------|--------|----------|-----------------------------------------------------------------------------|
| `Tls`                 | bool   | `true`   | Enable TLS transport security                                               |
| `TlsVerifyCert`       | bool   | `true`   | Verify the server TLS certificate                                           |
| `TlsCaFile`           | string | _none_   | Path to a custom CA certificate file                                        |
| `UserCertFile`        | string | _none_   | Path to client certificate (mTLS)                                           |
| `UserKeyFile`         | string | _none_   | Path to client private key (mTLS)                                           |
| `ConnectionName`      | string | auto     | Connection identifier for diagnostics                                       |
| `DefaultDeadline`     | int    | `30000`  | Client-side timeout in milliseconds (`-1` = infinite)                       |
| `KeepAliveInterval`   | int    | `60000`  | Keep-alive ping interval in milliseconds                                    |
| `KeepAliveTimeout`    | int    | `30000`  | Keep-alive response timeout in milliseconds                                 |
| `MaxDiscoverAttempts` | int    | `10`     | Maximum gossip discovery attempts                                           |
| `DiscoveryInterval`   | int    | `100`    | Milliseconds between discovery attempts                                     |
| `GossipTimeout`       | int    | `5000`   | Individual discovery attempt timeout in milliseconds                        |
| `NodePreference`      | enum   | `Random` | Node selection for reads: `Leader`, `Follower`, `ReadOnlyReplica`, `Random` |

### Examples

Development (no TLS):

```
kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false
```

Production with custom CA:

```
kurrentdb://admin:changeit@kurrentdb.internal:2113?tlsCaFile=/certs/ca.crt
```

Mutual TLS (certificate authentication):

```
kurrentdb://kurrentdb.internal:2113?userCertFile=/certs/client.crt&userKeyFile=/certs/client.key
```

Cluster with follower preference:

```
kurrentdb+discover://admin:changeit@node1:2113,node2:2113,node3:2113?nodePreference=Follower
```

## Creating a client

### From a connection string

```csharp
using Kurrent.Client;

await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113");
```

### From a connection string with overrides

`KurrentClient.Create(connectionString, configure)` parses the string into options first, then runs your callback so you can tune anything before the client is built:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@localhost:2113",
    opts => {
        opts.Resilience    = KurrentClientResilienceOptions.HighAvailability;
        opts.LoggerFactory = loggerFactory;
        opts.Schema.Mapper.Map<OrderPlaced>();
    });
```

### From a pre-built options instance

`KurrentClientOptions` is a record. Construct one directly when you want full control:

```csharp
var options = new KurrentClientOptions {
    ConnectionName   = "my-service",
    ConnectionScheme = KurrentConnectionScheme.Direct,
    Endpoints        = [new("kurrentdb.internal", 2113)],
    Security         = KurrentClientSecurityOptions.Basic("admin", "changeit"),
    Resilience       = KurrentClientResilienceOptions.HighAvailability
};

await using var client = KurrentClient.Create(options);
```

You can also start from a parsed connection string and use `with` to override individual sections:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://admin:changeit@localhost:2113") with {
    Resilience = KurrentClientResilienceOptions.HighAvailability
};

await using var client = KurrentClient.Create(options);
```

### Security options

Security is configured through `KurrentClientOptions.Security` using `KurrentClientSecurityOptions` factories:

```csharp
// Defaults: TLS enabled, server certificate verified, no client auth.
var options = KurrentClientOptions.Parse("kurrentdb://localhost:2113") with {
    Security = KurrentClientSecurityOptions.Default
};

// Insecure (development only).
options = options with {
    Security = KurrentClientSecurityOptions.Insecure
};

// Basic authentication (forces TLS).
options = options with {
    Security = KurrentClientSecurityOptions.Basic("admin", "changeit")
};

// Mutual TLS using file paths.
options = options with {
    Security = KurrentClientSecurityOptions.MutualTls(
        clientCertPath: "/path/to/client.crt",
        clientKeyPath:  "/path/to/client.key",
        rootCaPath:     "/path/to/ca.crt")
};
```

For finer-grained control you can compose the underlying variants directly:

```csharp
options = options with {
    Security = new() {
        Transport      = TransportSecurity.Certificate("/path/to/ca.crt"),
        Authentication = ClientCredentials.Certificate("/path/to/client.crt", "/path/to/client.key")
    }
};
```

## Resilience profiles

The client ships with predefined resilience configurations:

| Profile            | Description                                                   |
|--------------------|---------------------------------------------------------------|
| `Default`          | Resilient general-purpose configuration with infinite deadline and 3 retries. |
| `NoResilience`     | No retries, infinite deadline.                                |
| `FailFast`         | No retries, 5-second deadline. For latency-sensitive operations. |
| `HighAvailability` | 5 retries, 200ms fixed backoff, 30-second deadline.           |
| `CautiousWrite`    | 3 retries, 60-second deadline. Conservative for non-idempotent writes. |
| `Subscription`     | 8 retries, infinite deadline. Tuned for long-running subscriptions. |
| `RetryForever`     | Unlimited retries with exponential backoff.                   |

Apply a profile to options:

```csharp
options = options with {
    Resilience = KurrentClientResilienceOptions.HighAvailability
};
```

Or tune individual settings using `with`:

```csharp
options = options with {
    Resilience = KurrentClientResilienceOptions.HighAvailability with {
        Deadline          = TimeSpan.FromSeconds(10),
        KeepAliveInterval = TimeSpan.FromSeconds(30),
        KeepAliveTimeout  = TimeSpan.FromSeconds(15)
    }
};
```

## Dependency injection

Register `KurrentClient` as a singleton in `IServiceCollection`:

### From configuration

```csharp
services.AddKurrentClient();
```

This reads a connection string from `ConnectionStrings:KurrentDB` in `IConfiguration` (e.g. `appsettings.json`) and parses it into options. If neither a `KurrentClientOptions` instance nor a connection string is registered, registration throws.

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "KurrentDB": "kurrentdb://admin:changeit@localhost:2113"
  }
}
```

### From a connection string

```csharp
services.AddKurrentClient("kurrentdb://admin:changeit@localhost:2113");
```

### From a pre-built options instance

```csharp
services.AddKurrentClient(new KurrentClientOptions {
    ConnectionName = "my-service",
    Endpoints      = [new("kurrentdb.internal", 2113)],
    Resilience     = KurrentClientResilienceOptions.HighAvailability
});
```

### With a configure callback

`AddKurrentClient(Action<KurrentClientOptions>?)` resolves the options from the service provider, configuration, or defaults, and then runs your callback last so you can override anything:

```csharp
services.AddKurrentClient(opts => {
    opts.Resilience = KurrentClientResilienceOptions.HighAvailability;
});
```

All `AddKurrentClient` overloads use `TryAddSingleton`. If a client is already registered, subsequent calls are ignored.

### Service-provider overrides

Whichever overload you pick, the registration applies these overrides from the service provider before constructing the client (overriding any value already set on the options):

| Service                | Applied to                  |
|------------------------|-----------------------------|
| `MessageTypeMapper`    | `options.Schema.Mapper`     |
| `ILoggerFactory`       | `options.LoggerFactory`     |
| `IMetadataDecoder`     | `options.MetadataDecoder`   |

Registering `ILoggerFactory` in DI is enough to wire it into the client. No separate call is needed.

## Logging

When constructing a client outside DI, assign `LoggerFactory` directly:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@localhost:2113",
    opts => opts.LoggerFactory = loggerFactory);
```

## Disposing the client

`KurrentClient` implements `IAsyncDisposable`. Use `await using` or call `DisposeAsync()` explicitly:

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113");

// use client...
// disposed automatically at end of scope
```

When registered via dependency injection, disposal is handled by the DI container.
