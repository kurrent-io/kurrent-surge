---
order: 10
---

# Migration guide

Kurrent.Client is the official successor to the EventStoreDB .NET client (`EventStore.Client.Grpc.*` packages). This guide covers what changed, what still works, and how to move your application to the new client.

## Package changes

Replace the old EventStoreDB gRPC client packages with the single unified package:

::: code-tabs#shell

@tab dotnet CLI

```bash
# Remove old packages
dotnet remove package EventStore.Client.Grpc.Streams
dotnet remove package EventStore.Client.Grpc.PersistentSubscriptions
dotnet remove package EventStore.Client.Grpc.ProjectionManagement
dotnet remove package EventStore.Client.Grpc.Operations

# Add the new package
dotnet add package Kurrent.Client
```

@tab Package Manager

```powershell
# Remove old packages
Uninstall-Package EventStore.Client.Grpc.Streams
Uninstall-Package EventStore.Client.Grpc.PersistentSubscriptions
Uninstall-Package EventStore.Client.Grpc.ProjectionManagement
Uninstall-Package EventStore.Client.Grpc.Operations

# Add the new package
Install-Package Kurrent.Client
```

:::

The old client was split across multiple NuGet packages (`EventStore.Client.Grpc.Streams`, `EventStore.Client.Grpc.PersistentSubscriptions`, etc.). Kurrent.Client ships as a single package that provides access to all server features through a unified `KurrentClient` entry point.

## Namespace changes

All public types live under the `Kurrent.Client` root namespace and its feature-based sub-namespaces:

| Old namespace | New namespace |
|---|---|
| `EventStore.Client` | `Kurrent.Client` |
| `EventStore.Client.Streams` | `Kurrent.Client.Streams` |

Update your `using` directives:

```csharp
// Old
using EventStore.Client;

// New
using Kurrent.Client;
using Kurrent.Client.Streams;
```

## Connection string compatibility

Existing connection strings continue to work without changes. The client accepts the legacy `esdb://` and `eventstore://` schemes as aliases for the new `kurrentdb://` scheme:

| Legacy scheme | New equivalent | Description |
|---|---|---|
| `esdb://` | `kurrentdb://` | Direct connection to a single node |
| `esdb+discover://` | `kurrentdb+discover://` | Cluster connection with gossip discovery |
| `eventstore://` | `kurrentdb://` | Direct connection to a single node |
| `eventstore+discover://` | `kurrentdb+discover://` | Cluster connection with gossip discovery |

All of these are valid and produce identical client configurations:

```csharp
// Legacy schemes — still work
var client1 = KurrentClient.Create("esdb://admin:changeit@localhost:2113?tls=false");
var client2 = KurrentClient.Create("esdb+discover://admin:changeit@node1:2113,node2:2113,node3:2113");
var client3 = KurrentClient.Create("eventstore://admin:changeit@localhost:2113?tls=false");

// New scheme — recommended for new code
var client4 = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

All connection string parameters (`Tls`, `TlsVerifyCert`, `KeepAliveInterval`, `NodePreference`, etc.) work identically across all schemes. See the [Configuration](./configuration.md) page for the full parameter reference.

::: tip
Legacy connection strings stored in environment variables, configuration files, or secrets managers do not need to be changed immediately. The client normalizes all schemes internally.
:::

## Client initialization

The entry point changes from per-feature clients to a single `KurrentClient`:

```csharp
// Old — separate client per feature
var settings = EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false");
var streamsClient = new EventStoreClient(settings);

// New — single client, module-based access
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

The `KurrentClient` provides access to server features through module properties:

| Module | Access | Description |
|---|---|---|
| Streams | `client.Streams` | Append, read, delete, tombstone, and manage streams |
| Registry | `client.Registry` | Schema registry operations |

Operations that were top-level methods on the old client are now accessed through the appropriate module:

```csharp
// Old
await client.AppendToStreamAsync("my-stream", StreamState.Any, events);

// New
var result = await client.Streams.Append(
    new AppendStreamRequest("my-stream", ExpectedStreamState.Any, records)
);
```

## Builder API

The old client used `EventStoreClientSettings.Create()` to parse a connection string. The new client provides a fluent builder:

```csharp
// Old
var settings = EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113");
var client = new EventStoreClient(settings);

// New — from connection string
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113");

// New — fluent builder
await using var client = KurrentClientOptionsBuilder.Create()
    .ConfigureConnection(conn => conn
        .WithDirectConnection("localhost", 2113)
        .WithBasicAuthentication("admin", "changeit")
        .WithInsecureTransport())
    .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
    .Build()
    .CreateClient();

// New — builder from connection string with overrides
await using var client = KurrentClientOptionsBuilder
    .FromConnectionString("kurrentdb://admin:changeit@localhost:2113")
    .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
    .Build()
    .CreateClient();
```

## Error handling — Result pattern

The most significant API change is how errors are reported. The old client threw exceptions for expected failures (wrong expected version, stream not found, etc.). Kurrent.Client uses the **Result pattern** instead.

Public APIs return `Result<TSuccess, TError>` where `TError` is a variant type (discriminated union) over all possible error cases for that operation.

### Idiomatic style (throwing)

Methods ending in `Async` throw on failure, similar to the old client behavior. Use these for a simpler migration:

```csharp
// Old
var result = await client.AppendToStreamAsync("my-stream", StreamState.Any, events);

// New — idiomatic, throws on failure
var result = await client.Streams.AppendAsync("my-stream", record);
```

### Explicit style (Result-based)

Methods without the `Async` suffix return `Result<TSuccess, TError>`. Use these for precise error handling without exceptions:

```csharp
var result = await client.Streams.Append(
    new AppendStreamRequest("my-stream", ExpectedStreamState.Any, [record])
);

result.Switch(
    success => Console.WriteLine($"Revision: {success.StreamRevision}"),
    error => error.Switch(
        streamNotFound      => HandleMissing(),
        streamAlreadyExists => HandleDuplicate(),
        revisionConflict    => HandleConflict(revisionConflict),
        exception           => throw exception
    )
);
```

See the [Error handling](./error-handling.md) page for the full Result API reference.

## Event model changes

The old client used `EventData` for appending and `ResolvedEvent` / `RecordedEvent` for reading. Kurrent.Client uses `AppendRecord` and `Record`:

### Appending

```csharp
// Old
var eventData = new EventData(
    Uuid.NewUuid(),
    "OrderPlaced",
    JsonSerializer.SerializeToUtf8Bytes(new { OrderId = "123" })
);
await client.AppendToStreamAsync("order-123", StreamState.Any, new[] { eventData });

// New
var record = new AppendRecord("OrderPlaced", new { OrderId = "123" });
var result = await client.Streams.AppendAsync("order-123", record);
```

`AppendRecord` handles JSON serialization automatically. You pass the event type name and the data object directly.

### Reading

```csharp
// Old
var events = client.ReadStreamAsync(Direction.Forwards, "order-123", StreamPosition.Start);
await foreach (var resolvedEvent in events) {
    Console.WriteLine(resolvedEvent.Event.EventType);
}

// New
var stream = await client.Streams.ReadStreamRecordsAsync("order-123");
await foreach (var item in stream) {
    item.Switch(
        record => Console.WriteLine($"{record.EventType} at revision {record.Position.StreamRevision}"),
        heartbeat => { }
    );
}
```

## Dependency injection

```csharp
// Old
services.AddEventStoreClient("esdb://admin:changeit@localhost:2113?tls=false");

// New
services.AddKurrentClient("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

With builder overrides:

```csharp
services.AddKurrentClient(builder => builder
    .ConfigureResilience(KurrentClientResilienceOptions.HighAvailability)
    .WithLoggerFactory(loggerFactory));
```

## Step-by-step migration

1. **Replace NuGet packages.** Remove all `EventStore.Client.Grpc.*` packages and add `Kurrent.Client`.

2. **Update using directives.** Replace `using EventStore.Client;` with `using Kurrent.Client;` and `using Kurrent.Client.Streams;`.

3. **Update client construction.** Replace `new EventStoreClient(settings)` with `KurrentClient.Create(connectionString)`. Your existing `esdb://` connection strings work as-is.

4. **Update operation calls.** Move from top-level methods to module-based access:
   - `client.AppendToStreamAsync(...)` becomes `client.Streams.AppendAsync(...)` or `client.Streams.Append(...)`
   - `client.ReadStreamAsync(...)` becomes `client.Streams.ReadStreamRecordsAsync(...)` or `client.Streams.ReadStreamRecords(...)`
   - `client.DeleteAsync(...)` becomes `client.Streams.Delete(...)`
   - `client.TombstoneAsync(...)` becomes `client.Streams.Tombstone(...)`

5. **Update event types.** Replace `EventData` with `AppendRecord` and update reading code to work with `Record` instead of `ResolvedEvent`.

6. **Adopt Result pattern (optional).** Start with the idiomatic `Async` methods for a drop-in replacement, then migrate to explicit Result handling where you need fine-grained error control.

7. **Update connection strings (optional).** Replace `esdb://` with `kurrentdb://` at your own pace. Both work identically.

## Summary of breaking changes

| Area | Old client | New client |
|---|---|---|
| Package | Multiple `EventStore.Client.Grpc.*` packages | Single `Kurrent.Client` package |
| Root namespace | `EventStore.Client` | `Kurrent.Client` |
| Client type | `EventStoreClient` | `KurrentClient` |
| Client creation | `new EventStoreClient(settings)` | `KurrentClient.Create(connectionString)` |
| Settings type | `EventStoreClientSettings` | `KurrentClientOptions` |
| Operation access | Top-level methods on the client | Module properties (`client.Streams`, `client.Registry`) |
| Error handling | Exceptions for expected failures | `Result<TSuccess, TError>` pattern (with throwing convenience methods) |
| Append model | `EventData` | `AppendRecord` |
| Read model | `ResolvedEvent` / `RecordedEvent` | `Record` |
| Stream state | `StreamState` | `ExpectedStreamState` |
| Connection scheme | `esdb://` | `kurrentdb://` (legacy schemes still accepted) |
| Disposal | `IDisposable` | `IAsyncDisposable` (`await using`) |
