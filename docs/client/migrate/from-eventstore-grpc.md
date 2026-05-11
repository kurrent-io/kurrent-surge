---
order: 1
title: From EventStoreDB
---

# Migrate from `EventStore.Client.Grpc.*`

<CopyMarkdownButton />

Kurrent.Client is the official successor to the EventStoreDB .NET client (`EventStore.Client.Grpc.*` packages). This guide covers what changed, what still works, and how to move your application to the new client.

## Package changes

Replace the old EventStoreDB gRPC client packages with the unified package:

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

The old client was split across multiple NuGet packages. Kurrent.Client ships as a single package that provides access to the streams, registry, indexes, connectors, and admin modules through a unified `KurrentClient` entry point.

## Namespace changes

All public types live under the `Kurrent.Client` root namespace and feature-based sub-namespaces:

| Old namespace               | New namespace                |
|-----------------------------|------------------------------|
| `EventStore.Client`         | `Kurrent.Client`             |
| `EventStore.Client.Streams` | `Kurrent.Client.Streams`     |

Update your `using` directives.

**Old:**

```csharp
using EventStore.Client;
```

**New:**

```csharp
using Kurrent.Client;
using Kurrent.Client.Streams;
```

## Connection string compatibility

Existing connection strings continue to work without changes. The client accepts the legacy `esdb://` scheme as an alias for `kurrentdb://`:

| Legacy scheme         | New equivalent              | Description                                  |
|-----------------------|-----------------------------|----------------------------------------------|
| `esdb://`             | `kurrentdb://`              | Direct connection to a single node           |
| `esdb+discover://`    | `kurrentdb+discover://`     | Cluster connection with gossip discovery     |

All of these are valid and produce identical client configurations.

**Legacy schemes (still accepted):**

```csharp
var client1 = KurrentClient.Create("esdb://admin:changeit@localhost:2113?tls=false");
var client2 = KurrentClient.Create("esdb+discover://admin:changeit@node1:2113,node2:2113,node3:2113");
```

**New scheme (recommended for new code):**

```csharp
var client3 = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

All connection string parameters (`Tls`, `TlsVerifyCert`, `KeepAliveInterval`, `NodePreference`, etc.) work identically across schemes. See [Connection Options](../connect/connection-options.md) for the full parameter reference.

::: tip
Legacy connection strings stored in environment variables, configuration files, or secrets managers do not need to be changed immediately. The client normalizes all schemes internally.
:::

## Client initialization

The entry point changes from per-feature clients to a single `KurrentClient`.

**Old:** separate client per feature.

```csharp
var settings      = EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false");
var streamsClient = new EventStoreClient(settings);
```

**New:** single client, module-based access.

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

The `KurrentClient` provides access to server features through module properties:

| Module      | Access               | Description                                                |
|-------------|----------------------|------------------------------------------------------------|
| Streams     | `client.Streams`     | Append, read, delete, tombstone, and manage streams        |
| Registry    | `client.Registry`    | Schema registry operations                                 |
| Indexes     | `client.Indexes`     | User-defined indexes                                       |
| Connectors  | `client.Connectors`  | Built-in connector management                              |
| Admin       | `client.Admin`       | Cluster and server administration                          |

Operations that were top-level methods on the old client are now accessed through the appropriate module.

**Old:**

```csharp
await client.AppendToStreamAsync("my-stream", StreamState.Any, events);
```

**New:**

```csharp
await client.Streams.AppendStream("my-stream", ExpectedStreamState.Any, records);
```

## Programmatic configuration

The old client used `EventStoreClientSettings.Create()` to parse a connection string. The new client uses the `KurrentClientOptions` record.

**Old:**

```csharp
var settings = EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113");
var client   = new EventStoreClient(settings);
```

**New, from a connection string:**

```csharp
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113");
```

**New, from a connection string with overrides:**

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@localhost:2113",
    opts => {
        opts.Resilience    = KurrentClientResilienceOptions.HighAvailability;
        opts.LoggerFactory = loggerFactory;
    });
```

**New, from a pre-built options instance:**

```csharp
var options = new KurrentClientOptions {
    Endpoints  = [new("localhost", 2113)],
    Security   = KurrentClientSecurityOptions.Basic("admin", "changeit"),
    Resilience = KurrentClientResilienceOptions.HighAvailability
};

await using var client = KurrentClient.Create(options);
```

`KurrentClientOptions` is a record, so you can also start from a parsed connection string and use `with` to override sections:

```csharp
var options = KurrentClientOptions.Parse("kurrentdb://admin:changeit@localhost:2113") with {
    Resilience = KurrentClientResilienceOptions.HighAvailability
};
```

See [Connection Options](../connect/connection-options.md) for the full options reference.

## Error handling

The old client threw exceptions for expected failures (wrong expected version, stream not found, etc.). Kurrent.Client also throws, but the exceptions are well-typed and let you pattern-match by catching specific types.

**Old:**

```csharp
try {
    await client.AppendToStreamAsync("my-stream", new StreamRevision(4), events);
}
catch (WrongExpectedVersionException ex) {
    // ...
}
```

**New:**

```csharp
try {
    await client.Streams.AppendStream(record, Revision.From(4));
}
catch (StreamRevisionConflictException ex) {
    Console.WriteLine($"Expected {ex.ExpectedRevision}, was {ex.ActualRevision}");
}
```

See [Error handling](../error-handling.md) for the full exception list.

## Record model changes

The old client used `EventData` for appending and `ResolvedEvent` / `RecordedEvent` for reading. Kurrent.Client uses `AppendRecord` and `Record`:

### Appending

**Old:**

```csharp
var eventData = new EventData(
    Uuid.NewUuid(),
    "OrderPlaced",
    JsonSerializer.SerializeToUtf8Bytes(new { OrderId = "123" })
);
await client.AppendToStreamAsync("order-123", StreamState.Any, new[] { eventData });
```

**New:**

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new { OrderId = "123" })
    .Build();

var result = await client.Streams.AppendStream(record);
```

`AppendRecord` handles JSON serialization automatically. The schema name is derived from the .NET type using the configured naming strategy. See [Serialization](../serialization/README.md) for how to control this.

### Reading

**Old:**

```csharp
var events = client.ReadStreamAsync(Direction.Forwards, "order-123", StreamPosition.Start);
await foreach (var resolvedEvent in events) {
    Console.WriteLine(resolvedEvent.Event.EventType);
}
```

**New:**

```csharp
var request = ReadStreamRequest.New.ForStream("order-123").Build();

var stream = client.Streams.ReadStream(request);

await foreach (var item in stream) {
    item.Switch(
        record    => Console.WriteLine("{0} at revision {1}", record.TypeName, record.Revision),
        heartbeat => { }
    );
}
```

Read operations no longer use the `*Async` suffix. They return `IAsyncEnumerable<ReadRecordsResult>` directly, where each result is a discriminated union of `Record` or `Heartbeat`. For a quick read of an entire stream, the `ReadStreamRecords` extension skips the builder.

## Dependency injection

**Old:**

```csharp
services.AddEventStoreClient("esdb://admin:changeit@localhost:2113?tls=false");
```

**New:**

```csharp
services.AddKurrentClient("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

With a configure callback:

```csharp
services.AddKurrentClient(opts => {
    opts.Resilience = KurrentClientResilienceOptions.HighAvailability;
});
```

The DI registration also picks up `MessageTypeMapper`, `ILoggerFactory`, and `IMetadataDecoder` from the service provider automatically. See [Create a Client › Register with dependency injection](../connect/create-a-client.md#register-with-dependency-injection).

## Migration checklist

Work through the steps in order. Each step links to the relevant detailed section for full context.

### 1. Replace NuGet packages

Remove all `EventStore.Client.Grpc.*` packages and add the unified `Kurrent.Client` package. See [Package changes](#package-changes).

### 2. Update using directives

Replace `using EventStore.Client;` with `using Kurrent.Client;` and the feature-specific namespaces you actually use (for example, `using Kurrent.Client.Streams;`). See [Namespace changes](#namespace-changes).

### 3. Update client construction

Replace `new EventStoreClient(settings)` with `KurrentClient.Create(connectionString)`. Existing `esdb://` connection strings continue to work without changes. See [Client initialization](#client-initialization) and [Programmatic configuration](#programmatic-configuration).

### 4. Update operation calls

Top-level methods on the old client are now reached through module properties. The `Async` suffix has been removed.

| Old                                | New                                       |
|------------------------------------|-------------------------------------------|
| `client.AppendToStreamAsync(...)`  | `client.Streams.AppendStream(...)`        |
| `client.ReadStreamAsync(...)`      | `client.Streams.ReadStream(...)`          |
| `client.DeleteAsync(...)`          | `client.Streams.Delete(...)`              |
| `client.TombstoneAsync(...)`       | `client.Streams.Tombstone(...)`           |

### 5. Update record types

Replace `EventData` with `AppendRecord` on appends, and use `Record` instead of `ResolvedEvent`/`RecordedEvent` on reads.

| Old member                                  | New member                                                |
|---------------------------------------------|-----------------------------------------------------------|
| `EventData`                                 | `AppendRecord.New.ForStream(...).WithValue(...).Build()`  |
| `resolvedEvent.Event.EventType`             | `record.TypeName`                                         |
| `resolvedEvent.Event.OriginalEventNumber`   | `record.Revision`                                         |
| `resolvedEvent.Event.Position`              | `record.Position`                                         |

See [Record model changes](#record-model-changes).

### 6. Update concurrency checks

Replace `StreamRevision`/`StreamState` with `Revision`/`ExpectedStreamState`. `Revision` implicitly converts to `ExpectedStreamState`, so `Revision.From(4)` can be passed wherever `ExpectedStreamState` is expected.

### 7. Update error handling

Replace catches of `WrongExpectedVersionException` with `StreamRevisionConflictException`. Other domain failures surface as well-typed exceptions such as `StreamNotFoundException` and `StreamTombstonedException`. See [Error handling](#error-handling).

### 8. Update DI registration

Replace `services.AddEventStoreClient(...)` with `services.AddKurrentClient(...)`. See [Dependency injection](#dependency-injection).

### 9. Build and verify

Build the project. Compiler errors flag any remaining old API usage. Once the build succeeds, run your existing tests against a local KurrentDB instance to confirm behavior end to end.

### 10. Update connection strings (optional)

Replace `esdb://` with `kurrentdb://` at your own pace. Both schemes work identically and can coexist across services.

## Summary of breaking changes

| Area              | Old client                                     | New client                                                 |
|-------------------|------------------------------------------------|------------------------------------------------------------|
| Package           | Multiple `EventStore.Client.Grpc.*` packages   | Single `Kurrent.Client` package                            |
| Root namespace    | `EventStore.Client`                            | `Kurrent.Client`                                           |
| Client type       | `EventStoreClient`                             | `KurrentClient`                                            |
| Client creation   | `new EventStoreClient(settings)`               | `KurrentClient.Create(connectionString)`                   |
| Settings type     | `EventStoreClientSettings`                     | `KurrentClientOptions`                                     |
| Operation access  | Top-level methods on the client                | Module properties (`client.Streams`, `client.Registry`, …) |
| Error handling    | Exceptions for expected failures               | Well-typed exceptions per failure case                     |
| Append model      | `EventData`                                    | `AppendRecord` (built via `AppendRecord.New`)              |
| Read model        | `ResolvedEvent` / `RecordedEvent`              | `Record` (with `Heartbeat` interleaved on the stream)      |
| Stream state      | `StreamState`                                  | `ExpectedStreamState`                                      |
| Stream revision   | `StreamRevision`                               | `Revision`                                                 |
| Read method names | `*Async` suffix                                | No `Async` suffix, async return types                      |
| Connection scheme | `esdb://`                                      | `kurrentdb://` (legacy schemes still accepted)             |
| Disposal          | `IDisposable`                                  | `IAsyncDisposable` (`await using`)                         |
