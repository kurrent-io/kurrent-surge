---
order: 2
---

# Type mapping

<CopyMarkdownButton />

The mapper is the contract between your .NET code and the schema names KurrentDB stores. Code changes constantly. Schema names live forever. The mapper is where those two timelines meet.

## The contract problem

Every record on a stream carries a schema name. That name is part of the wire contract: cross-language consumers index on it, the registry stores schemas under it, replay tools group records by it. Once a record is written, its schema name is permanent.

.NET types change all the time. You rename `OrderShipped` to `OrderShippedV2` during a refactor. You move `Shop.Orders.OrderPlaced` to a new namespace. You split an assembly. Without something in between, every one of those changes silently breaks consumers reading the old records.

The mapper is that something in between. It's a bidirectional dictionary: encode picks the schema name for an outgoing .NET type, decode picks the .NET type for an incoming schema name. Renaming a class doesn't change the wire contract, because the mapper holds the wire name independently.

## Auto-mapping

The client auto-maps a type the first time it appears. The schema name comes from the active [naming strategy](./schema-names.md), so no setup is needed.

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced("order-123", "Widget"))
    .Build();

await client.Streams.AppendStream(record);
```

Auto-mapping is lazy. A record can only be decoded once its .NET type has been seen at least once in the current process. A consumer-only service never triggers auto-mapping, because no `AppendStream` ever runs locally.

## The cold consumer problem

A consumer that starts up fresh and reads records of a .NET type it has never appended in this process sees those records arrive raw. The serializer has no way to guess what `order.placed.v1` should decode into. It hands you bytes and the schema name, but no `OrderPlaced` instance.

This is the most common surprise in event-driven systems. Producer-only services work fine. Consumers that share types with producers through a NuGet package work fine. The trouble starts when a brand-new consumer rolls out against streams that already exist.

Consumers register every type they might receive at startup. Once registered, records arrive decoded the first time. The mapper is payload-agnostic: events, commands, and snapshots register the same way.

```csharp
var mapper = MessageTypeMapper.Create(m => m
    .Map<OrderPlaced>()      // event
    .Map<OrderShipped>()     // event
    .Map<PlaceOrder>()       // command
    .Map<OrderSnapshot>());  // snapshot

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Mapper = mapper);
```

The builder honors the configured naming strategy, so the generated schema names match what producers using the same strategy emit.

## Overriding the default name

The naming strategy gives you a default. The mapper is where you override it.

The most common reason that isn't about evolving an existing stream is cross-language consumers. A Go service indexing on `order.placed.v1` does not care about your .NET namespaces. Pick a stable, language-neutral name and pin it:

```csharp
.Map<OrderPlaced>("order.placed.v1")
```

Renaming a class, running multiple versions of a record on the same stream, and migrating between naming strategies are also mapper overrides. Each is covered end-to-end in [Schema evolution](./schema-evolution.md).

## Registration

`AddKurrentClient` honors any `MessageTypeMapper` registered in DI:

```csharp
services.AddSingleton(MessageTypeMapper.Create(m => m
    .Map<OrderPlaced>()
    .Map<PlaceOrder>()
    .Map<OrderSnapshot>()));

services.AddKurrentClient("kurrentdb://localhost:2113");
```

Registering the mapper in DI declares schema names once per service and is the safest default for consumers that read types they don't append locally. Share the same registration between producers and consumers so both sides agree on every wire name. When both DI and the options callback set a mapper, the options callback wins. See [Create a Client](../connect/create-a-client.md#service-provider-overrides) for the other services applied from DI.
