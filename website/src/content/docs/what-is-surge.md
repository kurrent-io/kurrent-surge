---
title: What is Kurrent Surge
description: The design, the goals, and what changed from the v1 KurrentDB client.
---

**Nothing is inferred.** Every schema name, every codec and every mapping is something you declared
and the compiler checked, not something the SDK guessed at runtime.

Kurrent Surge is the .NET SDK for KurrentDB and the Kurrent Platform. It replaces the v1
`KurrentDB.Client` packages, and the EventStoreDB clients before them. The v1 client was a transport:
it moved bytes to a stream and back, and everything above that was yours to build. Surge keeps that
surface and adds the layers most applications ended up writing themselves.

Three of those layers, in the order you meet them.

## Declare your messages

A record on a stream carries a schema name, a string identity that outlives your code. The v1 client
took the CLR type name and wrote that, so renaming a class silently broke every consumer. Surge makes
you say it. A **catalog** maps a type to a name, a source generator turns the declaration into code at
compile time, and an append of an undeclared type throws rather than writing a name you cannot change
later. For the full picture, see [Schema](/schema/).

```csharp
using Kurrent.Surge.Schema.Catalogs;

[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]

namespace Orders.Events;

[MessageType<OrderPlaced>("order.placed.v1")]
public record OrderPlaced(string OrderId, string Product, int Quantity);
```

Reading is deliberately softer. A record whose schema name has no mapping comes back raw, so an
unknown type from a newer producer does not kill your subscription.

## Append and read

One client reaches every module. Appends are transactional, reads are lazy, and both work in terms of
the types you declared above rather than bytes. See [Streams](/streams/).

```csharp
// tls=false is the local development mode. Production drops it: /client/security/
await using var client = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");

await client.Streams.AppendStream("order-123", [
    AppendRecord.Create(new OrderPlaced("order-123", "Widget", 5))
]);
```

## Process what arrives

Reading a stream by hand suits a query. A **processor** suits everything else: it owns the
subscription, the checkpoints, the retries, the batching and the partition assignment, and leaves you
one method. See [Processors](/processors/).

```csharp
public sealed class OrderProcessor : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) {
        if (context.Record.Value is OrderPlaced placed)
            Handle(placed);

        return ValueTask.CompletedTask;
    }
}
```

## How it is designed

**Built for Native AOT.** Catalogs, discriminated unions and Avro codecs come from source generators.
All four packages are `IsAotCompatible` and `IsTrimmable`, trim and AOT analysis runs unsuppressed
across the repository, and both the map from CLR type to schema name and the table of codecs are
frozen dictionaries built once at startup. JSON goes through a source-generated
`JsonSerializerContext`, with the reflection fallback behind a feature switch the linker removes.

**Serialization happens for you, in three formats.** You append a declared type and the SDK encodes
it. Every record carries its own format on the wire, so a reader picks the codec from the record
instead of from configuration, and a producer moving to Protobuf does not break a consumer. JSON is
the default, Protobuf and Avro are a property on the record you append, and raw bytes skip the layer
entirely. See [Formats](/schema/formats/).

**Resilience is configured on both halves.** The client retries at the gRPC channel, declared in the
service config so the transport enforces it rather than a loop above it. Processors retry inside the
runtime, on a pipeline you can extend or replace outright. The same vocabulary on both sides, and a
hand-rolled retry loop on neither. See [Configuring the client](/client/configuring-the-client/) and
[Error handling](/processors/error-handling/).

**Routing is a gRPC policy, not a wrapper.** Discovery, node ranking, connection replacement and
leader-change following all run inside the channel, as a load balancing policy registered on it. See
[Load balancing](/client/load-balancing/).

**One surface over two protocols.** Appends run on the V2 gRPC service. Reads and stream management
run on the V1 legacy service. Indexes and connectors use V2 messages. You call one client, and the
split stays an implementation detail.

**Failures have types.** Every operation throws a typed exception carrying an error code. There is no
result wrapper to unpack, and no single catch-all to inspect for a reason string.

**Install only what you need.** There is no meta-package.

| Package | What it is | Links |
|---|---|---|
| `Kurrent.Surge` | The client and the processors. What you install | [Docs](/client/) · [NuGet](https://www.nuget.org/packages/Kurrent.Surge) · [Source](https://github.com/kurrent-io/kurrent-sdk-dotnet/tree/main/src/Kurrent.Surge) |
| `Kurrent.Surge.Core` | Contracts and value types. No gRPC, no transport. Arrives with the client | [NuGet](https://www.nuget.org/packages/Kurrent.Surge.Core) · [Source](https://github.com/kurrent-io/kurrent-sdk-dotnet/tree/main/src/Kurrent.Surge.Core) |
| `Kurrent.Surge.Schema.Catalogs` | Message declaration and the catalog generator | [Docs](/schema/catalogs/) · [NuGet](https://www.nuget.org/packages/Kurrent.Surge.Schema.Catalogs) · [Source](https://github.com/kurrent-io/kurrent-sdk-dotnet/tree/main/src/Kurrent.Surge.Schema.Catalogs) |
| `Kurrent.Surge.Codecs.Avro` | The opt-in Avro codec | [Docs](/schema/formats/) · [NuGet](https://www.nuget.org/packages/Kurrent.Surge.Codecs.Avro) · [Source](https://github.com/kurrent-io/kurrent-sdk-dotnet/tree/main/src/Kurrent.Surge.Codecs.Avro) |

## What changed from the v1 client

| | v1 `KurrentDB.Client` | Kurrent Surge |
|---|---|---|
| Packages | Four, one per feature | One |
| Client | One type per feature | One client, five modules |
| Schema names | Inferred from the type name | Declared in a catalog |
| Errors | `WrongExpectedVersionException` and a catch-all | A type per failure, each with a code |
| Stream processing | Write the subscription loop yourself | A processor runtime |
| Serialization | Hand-rolled, by you | JSON, Protobuf or Avro through a codec |
| Schema registry | Not addressed | First-class, opt in |
| Secondary indexes | Not addressed | First-class |
| AOT | Reflection in the path | Source-generated, `IsAotCompatible` |
| Disposal | `IDisposable` | `IAsyncDisposable` |

Your connection strings keep working. `esdb://` and `eventstore://` still parse, and every parameter
means what it means in the other KurrentDB SDKs. See [Migration](/migration/) for the rest.

## What it is not

**Not a rewrite of your domain.** Surge serializes contracts you write. It has no opinion about
aggregates, commands or handlers.

**Not a projections engine.** Server-side projections stay on the server. Processors are a
client-side runtime for consuming streams.

**Not finished.** It is new, and it is built to grow into the rest of the platform without anyone
needing a second SDK.
