---
order: 2
---

# Type mapping

<CopyMarkdownButton />

A catalog is the contract between your .NET code and the schema names KurrentDB stores. Code changes constantly. Schema names live forever. The catalog is where those two timelines meet.

## The contract problem

Every record on a stream carries a schema name. That name is part of the wire contract: cross-language consumers index on it, the registry stores schemas under it, replay tools group records by it. Once a record is written, its schema name is permanent.

.NET types change all the time. You rename `OrderShipped` to `OrderShippedV2` during a refactor. You move `Shop.Orders.OrderPlaced` to a new namespace. You split an assembly. Without something in between, every one of those changes silently breaks consumers reading the old records.

The catalog is that something in between. It's a bidirectional mapping: append resolves the schema name for an outgoing .NET type, decode resolves the .NET type for an incoming schema name. Renaming a class doesn't change the wire contract, because the catalog holds the wire name independently.

## Vocabulary is declared, not inferred

The client never invents a mapping. The set of types it can append and decode is exactly what you declared — with [attributes on your contracts](./message-discovery.md) or with explicit `Map` calls. Appending a type with no mapping throws immediately: there is nothing safe to guess a permanent wire name from.

Inferring names from whatever type happens to pass by makes demos shorter and production incidents longer — an accidental refactor becomes an accidental contract change. Declaring the vocabulary makes the contract reviewable: it sits in code, and a diff to it reads as what it is, a wire contract change.

Each mapping resolves its name the same way: a pinned name wins, then a strategy, then the default convention (the full type name — see [Schema names](./schema-names.md)).

## Mapping types

The quickest path is mapping directly on the client options. These land in the client's default catalog:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages
        .MapMessage<OrderPlaced>("order.placed.v1")                       // pinned
        .MapMessage<OrderShipped>(SchemaNameStrategies.Namespace("acme")) // strategy
        .MapMessage<OrderSnapshot>());                                    // default convention
```

The mapping is payload-agnostic: events, commands, and snapshots map the same way.

To share one set of mappings across clients and services, build a catalog object and hand it to each client:

```csharp
var catalog = new MessageCatalog("orders")
    .Map<OrderPlaced>("order.placed.v1")
    .Map<OrderShipped>("order.shipped.v1")
    .Map<PlaceOrder>("order.place.v1");

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.UseCatalog(catalog));
```

A catalog is a mutable template: each client snapshots it at construction, so later changes to the catalog never affect a live client.

Most teams don't build catalogs by hand at all — they declare types with attributes and let the compiler build and wire the catalogs. That path, including automatic consumption across assemblies, is covered in [Message catalogs](./message-discovery.md).

## The cold consumer problem

A consumer that starts up fresh must decode records of types it has never appended. The serializer has no way to guess what `order.placed.v1` should decode into — the mapping has to exist before the read.

This is the most common surprise in event-driven systems, and declaration solves it structurally: the vocabulary ships with the contracts assembly, so a brand-new consumer that references the contracts package decodes from its very first record. Nothing depends on having appended first — append and read use the same declared catalog.

When a record's schema name has no mapping anyway — a producer deployed a type you don't know yet — the record passes through raw instead of failing the read: you get the bytes and the schema name, no decoded value. Strict consumers opt into failing via `ThrowOnUnresolved` on read and subscribe options. See [Decoding records](./decoding-records.md).

## Overriding the default name

The default convention gives you a name derived from the type. Pinning is how you override it.

The most common reason that isn't about evolving an existing stream is cross-language consumers. A Go service indexing on `order.placed.v1` does not care about your .NET namespaces. Pick a stable, language-neutral name and pin it:

```csharp
.MapMessage<OrderPlaced>("order.placed.v1")
```

Renaming a class, running multiple versions of a record on the same stream, and migrating between naming strategies are also pins. Each is covered end-to-end in [Schema evolution](./schema-evolution.md).

## Registration

`AddKurrentClient` picks up every `MessageCatalog` registered in DI and composes it into the client:

```csharp
services.AddSingleton(new MessageCatalog("orders")
    .Map<OrderPlaced>("order.placed.v1")
    .Map<PlaceOrder>("order.place.v1"));

services.AddKurrentClient("kurrentdb://localhost:2113");
```

Catalogs compose — DI-registered catalogs are added alongside anything configured on the options, never replacing it. Registering a catalog in DI declares schema names once per service; share the same catalog (or the same contracts package) between producers and consumers so both sides agree on every wire name. Two catalogs that disagree — the same schema name mapped to different types, or the same type to different names — fail client construction with an error naming the offending catalog. See [Create a Client](../connect/create-a-client.md#service-provider-overrides) for the other services applied from DI.
