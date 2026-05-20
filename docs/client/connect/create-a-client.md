---
order: 1
---

# Create a Client

<CopyMarkdownButton />

A single `KurrentClient` instance handles connections, retries, serialization, and routing for your entire application. It is thread-safe and reuses a single gRPC channel, so create one instance and share it across your code.

Configure it through a connection string or a `KurrentClientOptions` record.

::: tabs#create-client

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

For the scheme and endpoints to use against different deployments, see [Connection Targets](./connection-targets.md). For TLS and authentication, see [Security](./security.md). For retries, deadlines, and channel tuning, see [Connection Options](./connection-options.md).

## Registration

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

If neither a connection string nor a `KurrentClientOptions` instance is registered, the client throws on first resolution. The overloads that take a connection string or an options instance validate at registration time instead.

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

### Service provider overrides

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
