---
order: 3
---

# Connection Options

<CopyMarkdownButton />

Connection options control how the client handles retries, deadlines, and channel diagnostics. They apply on top of any [connection target](./connection-targets.md) and can be set in the connection string or on `KurrentClientOptions`. For TLS and authentication, see [Security](./security.md).

## Tune resilience

No profile is applied automatically. `Resilience` starts as `NoResilience` until you assign one. The client ships predefined profiles for common scenarios:

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
        Deadline          = TimeSpan.FromSeconds(45),
        KeepAliveInterval = TimeSpan.FromSeconds(30),
        KeepAliveTimeout  = TimeSpan.FromSeconds(10)
    });
```

The two-argument `Create` overload parses the connection string into options first, then runs the callback, so anything set in the callback wins.

## Connection string reference

These parameters apply to every connection string, whether you target a local node, a single production server, or a cluster. Names are case-insensitive. For TLS and authentication parameters, see [Security](./security.md#connection-string-parameters).

| Parameter             | Type   | Default  | Description                                                                     |
|-----------------------|--------|----------|---------------------------------------------------------------------------------|
| `connectionName`      | string | auto     | Connection identifier for diagnostics                                           |
| `defaultDeadline`     | int    | `30000`  | Client-side timeout in milliseconds (`-1` = infinite)                           |
| `keepAliveInterval`   | int    | `60000`  | Keep-alive ping interval in milliseconds (`-1` = infinite)                      |
| `keepAliveTimeout`    | int    | `30000`  | Keep-alive response timeout in milliseconds (`-1` = infinite)                   |
| `maxDiscoverAttempts` | int    | `10`     | Maximum gossip discovery attempts                                               |
| `discoveryInterval`   | int    | `100`    | Milliseconds between discovery attempts                                         |
| `gossipTimeout`       | int    | `5000`   | Individual discovery attempt timeout in milliseconds                            |
| `nodePreference`      | enum   | `Random` | Node selection for reads (`Leader` / `Follower` / `ReadOnlyReplica` / `Random`) |

Supported schemes: `kurrentdb` and `kurrentdb+discover`.
