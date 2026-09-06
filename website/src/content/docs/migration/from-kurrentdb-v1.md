---
title: From KurrentDB v1
description: Move a project from the KurrentDB.Client v1 packages onto Kurrent Surge.
---

Kurrent Surge is the successor to the v1 `KurrentDB.Client` packages. Because v1 was itself a rebrand
of the EventStoreDB client, this migration is the same shape as
[From EventStoreDB](/kurrent-surge/migration/from-eventstoredb/). Only the names you are replacing differ.

Your `kurrentdb://` connection strings keep working unchanged.

## Packages

```bash
dotnet remove package KurrentDB.Client.Streams
dotnet remove package KurrentDB.Client.PersistentSubscriptions
dotnet remove package KurrentDB.Client.ProjectionManagement
dotnet remove package KurrentDB.Client.Operations

dotnet add package Kurrent.Surge
```

Four packages become one. `Kurrent.Surge` carries streams, the schema registry, secondary indexes,
connectors, and node administration behind a single `KurrentClient`.

## Namespaces

The `DB` infix is gone.

| Old | New |
|---|---|
| `KurrentDB.Client` | `Kurrent.Surge.Client` |
| `KurrentDB.Client.Streams` | `Kurrent.Surge.Client.Streams` |

```csharp
// Old
using KurrentDB.Client;

// New
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
```

## One client, five modules

```csharp
// Old
var settings = KurrentDBClientSettings.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
var client   = new KurrentDBClient(settings);

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

`KurrentClientOptions` replaces `KurrentDBClientSettings`. It is a record, so `with` composes on top
of a parsed connection string.

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://admin:changeit@localhost:2113",
    opts => opts.Resilience = KurrentClientResilienceOptions.HighAvailability);

var options = KurrentClientOptions.Parse("kurrentdb://admin:changeit@localhost:2113") with {
    Resilience = KurrentClientResilienceOptions.HighAvailability
};
```

Resilience presets: `Default`, `NoResilience`, `FailFast`, `HighAvailability`, `CautiousWrite`,
`Subscription`, `RetryForever`.

Security presets: `Insecure`, `Tls`, `Password`, `MutualTls`.

## Records replace events

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

`AppendRecord` encodes as JSON by default, and the schema name comes from your message catalog rather
than a hand-written type string. See [Catalogs](/kurrent-surge/schema/catalogs/).

Reads interleave records with checkpoints:

```csharp
await foreach (var item in client.Streams.ReadStream("order-123")) {
    item.Switch(
        record    => Console.WriteLine("{0} at revision {1}", record.TypeName, record.Revision),
        checkpoint => { });
}
```

The `Async` suffix is gone; the methods are still async.

## Exceptions are typed

```csharp
// Old
catch (WrongExpectedVersionException ex) { }

// New
catch (StreamRevisionConflictException ex) {
    Console.WriteLine("Expected {0}, was {1}", ex.ExpectedRevision, ex.ActualRevision);
}
```

## Dependency injection

```csharp
// Old
services.AddKurrentDBClient("kurrentdb://admin:changeit@localhost:2113?tls=false");

// New
services.AddKurrentSurge(surge => surge
    .Connection("kurrentdb://admin:changeit@localhost:2113?tls=false"));
```

## Checklist

1. **Packages.** Remove all four `KurrentDB.Client.*`, and add `Kurrent.Surge`.
2. **Usings.** `KurrentDB.Client` becomes `Kurrent.Surge.Client`.
3. **Construction.** `new KurrentDBClient(settings)` becomes `KurrentClient.Create(connectionString)`,
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
   `ExpectedStreamState`.
7. **Exceptions.** Replace `WrongExpectedVersionException` with `StreamRevisionConflictException`.
8. **Registration.** `AddKurrentDBClient` becomes `AddKurrentSurge`, which takes a scope rather than
   a connection string. See [Dependency injection](/kurrent-surge/client/dependency-injection/).
9. **Declare your message types.** Kurrent Surge does not infer schema names, so an append of an
   undeclared type throws. This has no v1 equivalent and is the step most likely to surprise you.
   See [Catalogs](/kurrent-surge/schema/catalogs/).
10. **Build, then run your tests.**

## Breaking changes

| Area | v1 | Kurrent Surge |
|---|---|---|
| Package | Four `KurrentDB.Client.*` | One `Kurrent.Surge` |
| Namespace | `KurrentDB.Client` | `Kurrent.Surge.Client` |
| Client type | `KurrentDBClient` | `KurrentClient` |
| Construction | `new KurrentDBClient(settings)` | `KurrentClient.Create(...)` |
| Settings | `KurrentDBClientSettings` | `KurrentClientOptions` |
| Feature access | Top-level methods | Module properties |
| Append model | `EventData` | `AppendRecord` |
| Read model | `ResolvedEvent` or `RecordedEvent` | `Record`, with `Checkpoint` interleaved |
| Stream state | `StreamState` | `ExpectedStreamState` |
| Revision | `StreamRevision` | `Revision` |
| Method names | `*Async` suffix | No suffix |
| Disposal | `IDisposable` | `IAsyncDisposable` |
| Schema names | Inferred from the event type string | Declared in a catalog |
