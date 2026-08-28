---
title: Catalogs
description: Declare which CLR types are messages and what they are called on the wire.
---

A catalog maps a CLR type to a schema name. A source generator turns your declarations into a
discoverable catalog per assembly, with no runtime reflection.

## Scan a namespace

The common case. Every message type in the namespace joins the catalog:

```csharp
[assembly: MessageCatalog("Orders.Events", Key = "orders")]
```

`AlwaysActive = true` registers it into every client without an explicit opt-in:

```csharp
[assembly: MessageCatalog("Orders.Events", AlwaysActive = true)]
```

## Scan by marker interface

Only types implementing the marker are included:

```csharp
[assembly: MessageCatalog<IDomainEvent>("Orders.Events", Key = "orders")]
```

Useful when a namespace holds more than messages.

## Pin one type

For a specific wire name, or for a type you do not own:

```csharp
[MessageType<OrderPlaced>("orders.order-placed.v1")]
public record OrderPlaced(string OrderId);

// A type from a referenced assembly, declared at the assembly level
[assembly: MessageType<ExternalInvoiceIssued>("external.invoice-issued.v1")]
```

## Naming strategies

Without an explicit name, a strategy derives one. The default is the type name; others use the
namespace, the category, or a combination. A strategy can also be pinned per type, and the generic
constraint makes it compile-checked:

```csharp
[MessageType<OrderArchived, KebabNamesStrategy>(Catalog = "orders")]
public record OrderArchived(string Id);
```

## Where it lives

Catalogs come from `Kurrent.Surge.Schema.Catalogs`. `Kurrent.Surge` already references it, so you
only install it directly in a contracts or domain assembly that must declare its messages without
taking the client.

That separation is the point: a domain assembly can declare its wire vocabulary without depending on
gRPC or on the client.
