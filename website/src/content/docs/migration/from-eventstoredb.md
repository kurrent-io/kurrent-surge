---
title: From EventStoreDB
description: Move a project from the EventStore.Client.Grpc packages onto Kurrent Surge.
---

Kurrent Surge is the successor to the EventStoreDB .NET client. This guide covers what changed, what
keeps working, and the order to change things in.

Your `esdb://` connection strings keep working. See [Migration](/migration/) for the full scheme
table. Everything below is about packages, namespaces, and API shape.

## Packages

The old client shipped as four packages. `Kurrent.Surge` is one.

```bash
dotnet remove package EventStore.Client.Grpc.Streams
dotnet remove package EventStore.Client.Grpc.PersistentSubscriptions
dotnet remove package EventStore.Client.Grpc.ProjectionManagement
dotnet remove package EventStore.Client.Grpc.Operations

dotnet add package Kurrent.Surge
```

`Kurrent.Surge` carries streams, the schema registry, secondary indexes, connectors, and node
administration behind one `KurrentClient`.

## Namespaces

| Old | New |
|---|---|
| `EventStore.Client` | `Kurrent.Surge.Client` |
| `EventStore.Client.Streams` | `Kurrent.Surge.Client.Streams` |

```csharp
// Old
using EventStore.Client;

// New
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
```

## One client, five modules

The old client gave you a type per feature. Kurrent Surge gives you one client, and reaches features
through module properties.

```csharp
// Old
var settings = EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false");
var client   = new EventStoreClient(settings);

// New
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

Note the `await using`. `IKurrentClient` is `IAsyncDisposable`, not `IDisposable`.

| Module | Reached through | Covers |
|---|---|---|
| Streams | `client.Streams` | Append, read, delete, tombstone, truncate, metadata |
| Registry | `client.Registry` | Schema registry |
| Indexes | `client.Indexes` | Secondary indexes |
| Connectors | `client.Connectors` | Connector lifecycle |
| Admin | `client.Admin` | Scavenge, shutdown, leader resignation, node priority |

```csharp
// Old
await client.AppendToStreamAsync("my-stream", StreamState.Any, events);

// New
await client.Streams.AppendStream("my-stream", records, ExpectedStreamState.Any);
```

## Configuration

`KurrentClientOptions` replaces `EventStoreClientSettings`. It is a record, so `with` composes on top
of a parsed connection string.

```csharp
// From a connection string
await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113");

// With overrides
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@localhost:2113",
    opts => {
        opts.Resilience    = KurrentClientResilienceOptions.HighAvailability;
        opts.LoggerFactory = loggerFactory;
    });

// From a parsed connection string
var options = KurrentClientOptions.Parse("kurrentdb://admin:changeit@localhost:2113") with {
    Resilience = KurrentClientResilienceOptions.HighAvailability
};
```

Resilience presets: `Default`, `NoResilience`, `FailFast`, `HighAvailability`, `CautiousWrite`,
`Subscription`, `RetryForever`.

Security presets: `Insecure`, `Tls`, `Password`, `MutualTls`.

## Records replace events

`EventData` becomes `AppendRecord`. `ResolvedEvent` and `RecordedEvent` become `Record`.

```csharp
// Old
var eventData = new EventData(
    Uuid.NewUuid(),
    "OrderPlaced",
    JsonSerializer.SerializeToUtf8Bytes(new { OrderId = "123" }));

await client.AppendToStreamAsync("order-123", StreamState.Any, new[] { eventData });

// New
var record = new AppendRecord {
    Stream = "order-123",
    Value  = new { OrderId = "123" }
};

var result = await client.Streams.AppendStream(record);
```

You no longer serialize by hand. `AppendRecord` encodes as JSON by default, and the schema name comes
from your message catalog. See [Catalogs](/schema/catalogs/).

Reads return an async sequence that interleaves records with checkpoints:

```csharp
// Old
var events = client.ReadStreamAsync(Direction.Forwards, "order-123", StreamPosition.Start);

await foreach (var resolvedEvent in events)
    Console.WriteLine(resolvedEvent.Event.EventType);

// New
await foreach (var item in client.Streams.ReadStream("order-123")) {
    item.Switch(
        record    => Console.WriteLine("{0} at revision {1}", record.TypeName, record.Revision),
        checkpoint => { });
}
```

The `Async` suffix is gone. The methods are still async, and return `IAsyncEnumerable<ReadMessage>`
or a `Task`.

## Exceptions are typed

The old client threw `WrongExpectedVersionException` for concurrency conflicts. Kurrent Surge throws
a distinct type per failure, so you catch the case you actually handle.

```csharp
// Old
catch (WrongExpectedVersionException ex) { }

// New
catch (StreamRevisionConflictException ex) {
    Console.WriteLine("Expected {0}, was {1}", ex.ExpectedRevision, ex.ActualRevision);
}
```

Also available: `StreamNotFoundException`, `StreamAlreadyExistsException`, `StreamDeletedException`,
`StreamTombstonedException`, `LogPositionNotFoundException`,
`MaxAppendRecordSizeExceededException`, `MaxAppendTransactionSizeExceededException`.

A read throws when you first iterate it, not when you call it. Wrap the `await foreach`, not the
call to `ReadStream`.

## Dependency injection

```csharp
// Old
services.AddEventStoreClient("esdb://admin:changeit@localhost:2113?tls=false");

// New
services.AddKurrentClient("kurrentdb://admin:changeit@localhost:2113?tls=false");
```

## Checklist

1. **Packages.** Remove all four `EventStore.Client.Grpc.*`, and add `Kurrent.Surge`.
2. **Usings.** `EventStore.Client` becomes `Kurrent.Surge.Client`, plus the feature namespaces you
   use.
3. **Construction.** `new EventStoreClient(settings)` becomes `KurrentClient.Create(connectionString)`,
   and `using` becomes `await using`.
4. **Calls.** Route through the module and drop the `Async` suffix:

   | Old | New |
   |---|---|
   | `client.AppendToStreamAsync(...)` | `client.Streams.AppendStream(...)` |
   | `client.ReadStreamAsync(...)` | `client.Streams.ReadStream(...)` |
   | `client.DeleteAsync(...)` | `client.Streams.Delete(...)` |
   | `client.TombstoneAsync(...)` | `client.Streams.Tombstone(...)` |

5. **Record types.** `EventData` becomes `AppendRecord`, and `ResolvedEvent` or `RecordedEvent`
   becomes `Record`:

   | Old | New |
   |---|---|
   | `resolvedEvent.Event.EventType` | `record.TypeName` |
   | `resolvedEvent.Event.OriginalEventNumber` | `record.Revision` |
   | `resolvedEvent.Event.Position` | `record.Position` |

6. **Concurrency.** `StreamRevision` becomes `Revision`, and `StreamState` becomes
   `ExpectedStreamState`. `Revision` converts implicitly, so `Revision.From(4)` passes anywhere
   `ExpectedStreamState` is wanted.
7. **Exceptions.** Replace `WrongExpectedVersionException` with `StreamRevisionConflictException`.
8. **Registration.** `AddEventStoreClient` becomes `AddKurrentClient`.
9. **Declare your message types.** This step has no equivalent in the old client and is the one that
   will bite you. Kurrent Surge does not infer schema names, so an append of an undeclared type throws.
   See [Catalogs](/schema/catalogs/).
10. **Build, then run your tests.** The compiler finds the remaining call sites.
11. **Connection strings, whenever you like.** `esdb://` keeps working indefinitely.

## Breaking changes

| Area | Old | Kurrent Surge |
|---|---|---|
| Package | Four `EventStore.Client.Grpc.*` | One `Kurrent.Surge` |
| Namespace | `EventStore.Client` | `Kurrent.Surge.Client` |
| Client type | `EventStoreClient` | `KurrentClient` |
| Construction | `new EventStoreClient(settings)` | `KurrentClient.Create(...)` |
| Settings | `EventStoreClientSettings` | `KurrentClientOptions` |
| Feature access | Top-level methods | Module properties |
| Append model | `EventData` | `AppendRecord` |
| Read model | `ResolvedEvent` or `RecordedEvent` | `Record`, with `Checkpoint` interleaved |
| Stream state | `StreamState` | `ExpectedStreamState` |
| Revision | `StreamRevision` | `Revision` |
| Method names | `*Async` suffix | No suffix |
| Disposal | `IDisposable` | `IAsyncDisposable` |
| Schema names | Inferred from the event type string | Declared in a catalog |
