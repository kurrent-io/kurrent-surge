---
order: 2
---

# Configuration

`KurrentClient` can be configured via connection strings, the fluent builder API, `appsettings.json`, or dependency injection. All approaches produce the same `KurrentClientOptions` — choose the one that fits your application.

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
| `esdb`               | Legacy scheme — alias for `kurrentdb`          |
| `esdb+discover`      | Legacy scheme — alias for `kurrentdb+discover` |

### Connection string parameters

| Parameter             | Type   | Default  | Description                                                                 |
|-----------------------|--------|----------|-----------------------------------------------------------------------------|
| `Tls`                 | bool   | `true`   | Enable TLS transport security                                               |
| `TlsVerifyCert`       | bool   | `true`   | Verify the server TLS certificate                                           |
| `TlsCaFile`           | string | —        | Path to a custom CA certificate file                                        |
| `UserCertFile`        | string | —        | Path to client certificate (mTLS)                                           |
| `UserKeyFile`         | string | —        | Path to client private key (mTLS)                                           |
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

## Builder API

The `KurrentClientOptionsBuilder` provides a fluent API for programmatic configuration:

```csharp
using Kurrent.Client;

var client = KurrentClientOptionsBuilder.Create()
    .ConfigureConnection(conn => conn
        .WithConnectionName("my-service")
        .WithDirectConnection("kurrentdb.internal", 2113)
        .WithBasicAuthentication("admin", "changeit")
        .WithTransportSecurity())
    .Build()
    .CreateClient();
```

### Starting from a connection string

Use a connection string as a base and override specific settings:

```csharp
var client = KurrentClientOptionsBuilder
    .FromConnectionString("kurrentdb://admin:changeit@localhost:2113")
    .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
    .WithLoggerFactory(loggerFactory)
    .Build()
    .CreateClient();
```

### Cluster discovery

```csharp
var client = KurrentClientOptionsBuilder.Create()
    .ConfigureConnection(conn => conn
        .WithGossipEndpoint("node1", 2113)
        .WithGossipEndpoint("node2", 2113)
        .WithGossipEndpoint("node3", 2113)
        .WithBasicAuthentication("admin", "changeit")
        .ConfigureGossip(gossip => gossip with {
            ReadPreference = NodePreference.Follower
        }))
    .Build()
    .CreateClient();
```

### Security options

Insecure (development only):

```csharp
.ConfigureConnection(conn => conn.WithInsecureTransport())
```

TLS with custom CA:

```csharp
.ConfigureConnection(conn => conn
    .ConfigureSecurity(KurrentClientSecurityOptions.Default with {
        Transport = TransportSecurity.Certificate("/path/to/ca.crt")
    }))
```

Mutual TLS (client certificate):

```csharp
.ConfigureConnection(conn => conn
    .WithCertificateAuthentication("/path/to/client.crt", "/path/to/client.key"))
```

## Resilience profiles

The client ships with predefined resilience configurations:

| Profile            | Description                                                   |
|--------------------|---------------------------------------------------------------|
| `NoResilience`     | No retries, infinite deadline. Default for new clients.       |
| `FailFast`         | No retries, short deadline. For latency-sensitive operations. |
| `HighAvailability` | Aggressive retries with backoff. For production clusters.     |
| `CautiousWrite`    | Conservative retries for write operations.                    |
| `Subscription`     | Tuned for long-running subscriptions.                         |
| `RetryForever`     | Unlimited retries with exponential backoff.                   |

```csharp
.ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
```

Or configure individual settings:

```csharp
.ConfigureResilience(resilience => resilience with {
    Deadline          = TimeSpan.FromSeconds(10),
    KeepAliveInterval = TimeSpan.FromSeconds(30),
    KeepAliveTimeout  = TimeSpan.FromSeconds(15)
})
```

## Dependency injection

Register `KurrentClient` as a singleton in `IServiceCollection`:

### From configuration

```csharp
services.AddKurrentClient();
```

This reads from the `Kurrent:Client` section in `appsettings.json` (see below). Falls back to defaults if no configuration is available.

### From a connection string

```csharp
services.AddKurrentClient("kurrentdb://admin:changeit@localhost:2113");
```

### With builder overrides

Configuration from `appsettings.json` is applied first, then the builder action overrides:

```csharp
services.AddKurrentClient(builder => builder
    .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
    .WithLoggerFactory(loggerFactory));
```

### With access to `IServiceProvider`

```csharp
services.AddKurrentClient((builder, sp) => {
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    builder.WithLoggerFactory(loggerFactory);
});
```

All `AddKurrentClient` overloads use `TryAddSingleton` — if a client is already registered, subsequent calls are ignored.

## Configuration from appsettings.json

The client can bind configuration from the `Kurrent:Client` section:

```json
{
  "Kurrent": {
    "Client": {
      "ConnectionString": "kurrentdb://admin:changeit@localhost:2113",
      "ConnectionName": "my-service",
      "Resilience": {
        "Deadline": "00:00:10",
        "KeepAliveInterval": "00:01:00",
        "KeepAliveTimeout": "00:00:30"
      },
      "Gossip": {
        "MaxDiscoverAttempts": 10,
        "DiscoveryInterval": "00:00:00.100",
        "Timeout": "00:00:05"
      }
    }
  }
}
```

The configuration layering order:

1. `ConnectionString` is parsed first to establish base settings (endpoints, auth, TLS)
2. `Resilience`, `Gossip`, and `Schema` sections override the parsed values
3. Fluent builder methods override everything

## Logging

Pass an `ILoggerFactory` to enable client diagnostics:

```csharp
var client = KurrentClientOptionsBuilder
    .FromConnectionString("kurrentdb://admin:changeit@localhost:2113")
    .WithLoggerFactory(loggerFactory)
    .Build()
    .CreateClient();
```

When using dependency injection, the logger factory from the service provider is not wired automatically — pass it explicitly via the builder overload:

```csharp
services.AddKurrentClient((builder, sp) => {
    builder.WithLoggerFactory(sp.GetRequiredService<ILoggerFactory>());
});
```

## Disposing the client

`KurrentClient` implements `IAsyncDisposable`. Use `await using` or call `DisposeAsync()` explicitly:

```csharp
await using var client = KurrentClient.Create("kurrentdb://localhost:2113");

// use client...
// disposed automatically at end of scope
```

When registered via dependency injection, disposal is handled by the DI container.
