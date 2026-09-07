---
title: Catalogs
description: Declare which message types a client speaks, and what they are called on the wire.
---

A catalog is the contract between your CLR types and the schema names KurrentDB stores. Code changes
constantly. Schema names live forever. The catalog is where those two timelines meet.

It is bidirectional: on append it resolves the schema name for an outgoing type, on read it resolves
the type for an incoming name. Rename a class and the wire contract does not move, because the
catalog holds the wire name independently.

## Declare a type

Put the attribute next to the contract. A source generator in `Kurrent.Surge.Core` turns
it into a catalog at compile time. There is no reflection, and the result is AOT-safe.

```csharp
[MessageType<OrderPlaced>("order.placed.v1")]
public record OrderPlaced(string OrderId, string Product, int Quantity);
```

Every client in every service that references the assembly now appends and decodes `OrderPlaced`
under `order.placed.v1`.

Each declaration carries its own naming source, resolved in a fixed order: **a pinned name wins,
then a strategy, then the default convention** (the full CLR type name).

```csharp
// Pinned. The string is the wire name, permanently decoupled from the CLR identifier.
[MessageType<OrderPlaced>("order.placed.v1")]
public record OrderPlaced(string OrderId);

// Strategy. Compile-checked: AcmeNames must implement ISchemaNameStrategy
// and have a parameterless constructor.
public sealed class AcmeNames() : NamespaceSchemaNameStrategy("acme", SchemaNameOutputFormat.KebabCase);

[MessageType<OrderShipped, AcmeNames>]          // acme.order-shipped
public record OrderShipped(string OrderId);

// Default convention. The full type name.
[MessageType<OrderCancelled>]                    // Acme.Orders.OrderCancelled
public record OrderCancelled(string OrderId);
```

Pin anything long-lived. See [Schema names](/kurrent-surge/schema/names/) for why.

## Types you do not own

The same attributes work at assembly level, so a type from a referenced package can be declared
without touching it:

```csharp
[assembly: MessageType<ThirdParty.InvoiceIssued>("invoice.issued.v1")]
[assembly: MessageType<ThirdParty.PaymentSettled>]
```

## Scan a namespace

When a whole namespace is messages, declare the namespace rather than each type:

```csharp
[assembly: MessageCatalog("Acme.Orders",  Key = "orders")]
[assembly: MessageCatalog("Acme.Billing", Key = "billing", Strategy = typeof(AcmeNames))]
[assembly: MessageCatalog<IDomainEvent>("Acme.Audit", Key = "audit")]
```

A scan-level `Strategy` names every type it picks up. The generic form constrains the scan to types
implementing or deriving from the given type, which keeps scans surgical in an assembly that mixes
messages with everything else.

`MessageCatalogAttribute` carries five knobs:

| Property | Effect |
|---|---|
| `NamespacePrefix` | Constructor argument. The namespace to scan |
| `Key` | Names the catalog. Omitted, declarations land in `"Default"` |
| `Strategy` | An `ISchemaNameStrategy` applied to every type in the scan |
| `AlwaysActive` | Registers this catalog into every client with no opt-in |
| `MustImplement` | Restricts the scan to types assignable to this type |

A scan is still a declaration. You wrote the attribute, so you own what it covers. But every type it
picks up gets a convention-derived name. For contracts that outlive the code, prefer per-type pins.

## Selecting catalogs

Discovery always runs: every catalog the referenced assemblies export is found, and each gets a
generated `Use*Catalog()` extension. A client then registers the catalogs marked always-active, plus
the ones you name.

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.UseOrdersCatalog());
```

A catalog that must be in *every* client declares itself:

```csharp
[assembly: MessageCatalog("Acme.Orders", Key = "orders", AlwaysActive = true)]
```

Always-active is not auto-mapping. What is automatic is *registering the catalog*; its *content* was
always declared by its author. Every mapping traces back to an attribute or a `Map` call someone
wrote.

## Hand-built catalogs

The attributes are a front end. The catalog object underneath is public, so build one when the
declarations need to be computed, shared, or kept out of the contracts assembly.

```csharp
var catalog = new MessageCatalog("orders")
    .Map<OrderPlaced>("order.placed.v1")                        // pinned
    .Map<OrderShipped>(SchemaNameStrategies.Namespace("acme"))  // strategy
    .Map<OrderCancelled>();                                     // default convention

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.UseCatalog(catalog));
```

Mark it `[CustomMessageCatalog]` and it surfaces exactly like a declared one: as
`MessageCatalogs.Orders`, through `MessageCatalogs.Get("orders")`, and with a generated
`UseOrdersCatalog()`:

```csharp
[CustomMessageCatalog]
sealed class OrdersCatalog() : MessageCatalog("orders", alwaysActive: true, c => c
    .Map<OrderPlaced>("order.placed.v1")
    .Map<OrderShipped>("order.shipped.v1"));
```

For a one-off mapping with no catalog object, map straight onto the options. It lands in the
client's `"Default"` catalog:

```csharp
opts.Messages.MapMessage<OrderPlaced>("order.placed.v1");
```

A catalog is a mutable template. Clients snapshot it at construction, so changing one later never
affects a live client.

## Conflicts fail at startup

A client unions everything at construction: the default catalog, explicit `UseCatalog` calls, and
every always-active catalog. Identical mappings arriving twice union harmlessly.

There are no override semantics. Two catalogs that map either the same schema name to different types
or the same type to different names **fail client construction**, with an error naming the offending
catalog. A conflicting vocabulary is a configuration bug, and it surfaces at startup rather than
mid-stream.

## The cold consumer

A brand-new consumer must decode types it has never appended. Nothing can guess what
`order.placed.v1` decodes into, so the mapping has to exist before the first read.

Declaration solves this structurally: the vocabulary ships with the contracts assembly, so a
consumer that references the contracts package decodes from its very first record. This is why a
dedicated contracts package is the natural unit of ownership. It carries the types, the attributes,
and therefore the catalogs.
