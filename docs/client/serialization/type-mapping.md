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

## The cold consumer problem

A consumer that starts up fresh and reads events for a .NET type it has never produced sees those events arrive raw. The serializer has no way to guess what `order.placed.v1` should decode into. It hands you bytes and the schema name, but no `OrderPlaced` instance.

This is the most common surprise in event-driven systems. Producer-only services work fine. Consumers that share types with producers through a NuGet package work fine. The trouble starts when a brand-new consumer rolls out against streams that already exist.

The fix is to register every type the consumer might receive at startup. Once registered, records arrive decoded the first time.

```csharp
var mapper = MessageTypeMapper.Create(m => m
    .Map<OrderPlaced>()
    .Map<OrderConfirmed>()
    .Map<OrderShipped>());

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Mapper = mapper);
```

The builder honors the configured naming strategy, so the generated schema names match what producers using the same strategy emit.

## Pinning a name explicitly

The naming strategy gives you a default. The mapper is where you override it. Common reasons:

**Cross-language consumers read the stream.** A Go service indexing on `order.placed.v1` doesn't care about your .NET namespaces. Pick a stable, language-neutral name and pin it.

```csharp
.Map<OrderPlaced>("order.placed.v1")
```

**A .NET rename has to leave the wire contract intact.** Six months of records sit on the stream under `order.shipped.v1`. The class is now `OrderShippedV2` because the team agreed on a clearer name. Pin the old wire name and the rename ships safely.

```csharp
.Map<OrderShippedV2>("order.shipped.v1")
```

**Two versions of an event coexist on the same stream.** When a shape change is not backward compatible, both versions live on the stream until v1 ages out. Each version is a distinct .NET type with a distinct schema name.

```csharp
.Map<OrderShippedV1>("order.shipped.v1")
.Map<OrderShippedV2>("order.shipped.v2")
```

For the full story on evolving stored events, see [Schema evolution](./schema-evolution.md).

## Auto-mapping is for prototypes

The client auto-maps a type the first time it appears. The schema name comes from the active [naming strategy](./schema-names.md), so no setup is needed.

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced("order-123", "Widget"))
    .Build();

await client.Streams.AppendStream(record);
```

Auto-mapping is lazy. A record can only be decoded once its .NET type has been seen at least once in the current process. The cold consumer problem still applies: a consumer-only service never triggers auto-mapping, because no `AppendStream` ever runs locally. For anything beyond a dev loop, register up front.

## Registration

A `MessageTypeMapper` registered in DI is applied automatically by `AddKurrentClient`. See [Create a Client](../connect/create-a-client.md#service-provider-overrides).
