---
order: 1
---

# Schema names

<CopyMarkdownButton />

Every record carries a string identity for its type, the **schema name**. The schema name is what readers use to resolve the .NET type to decode into. By default Kurrent derives the schema name from the type itself. You can pick a different strategy or override the name per type.

Let's say you have a basic record type in the `Acme.Orders` namespace:

```csharp
namespace Acme.Orders;

public record OrderPlaced(string OrderId, string Product);
```

By default, Kurrent identifies this type using its .NET full name, so a record of `OrderPlaced` produces a schema name of `Acme.Orders.OrderPlaced`.

## Strategies

The active strategy is set on the client options. Four factories ship on `SchemaNameStrategies`. Examples below assume the type is `Acme.Orders.OrderPlaced` and the stream is `order-123`.

| Strategy                    | Output                          |
|-----------------------------|---------------------------------|
| `TypeName()` (default)      | `Acme.Orders.OrderPlaced`       |
| `Category()`                | `order.OrderPlaced`             |
| `Namespace("acme")`         | `acme.OrderPlaced`              |
| `NamespaceCategory("acme")` | `acme.order.OrderPlaced`        |

`Category` and `NamespaceCategory` derive the category from the stream name (everything before the first `-`). They throw `ArgumentException` when the stream is empty, so reach for them only when every record has a stream segment to derive from.

Set the strategy on the client:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.NameStrategy = SchemaNameStrategies.Category());
```

## Output formats

Every factory accepts a `SchemaNameOutputFormat` that transforms the generated name. Examples below all use `TypeName()`.

| Format             | Output                                            |
|--------------------|---------------------------------------------------|
| `None` (default)   | `Acme.Orders.OrderPlaced`                         |
| `KebabCase`        | `acme.orders.order-placed`                        |
| `SnakeCase`        | `acme.orders.order_placed`                        |
| `Urn`              | `urn:schemas-kurrent:acme.orders:order-placed`    |

Casing transforms apply per identifier. Dots between namespace segments are preserved, so `Acme.Orders` stays `acme.orders` rather than collapsing into `acme-orders`.

Combine a strategy with a format:

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Namespace("acme", SchemaNameOutputFormat.KebabCase);

// Auto-mapped schema name for OrderPlaced: "acme.order-placed"
```

## Overriding the name per type

When the generated name is not what you want, override it explicitly through the type mapper:

```csharp
opts.Schema.Mapper = MessageTypeMapper.Create(m => m
    .Map<OrderPlaced>()
    .Map<OrderShipped>("order.shipped.v1"));
```

The string passed to `Map<T>` becomes the wire-level schema name and bypasses the active strategy. See [Type mapping](./type-mapping.md) for the full mapper API.

## Picking a strategy

| Scenario                                                | Reach for                  |
|---------------------------------------------------------|----------------------------|
| Internal-only events, CLR identity preserved            | `TypeName()`               |
| Events grouped by stream category, type name is unique  | `Category()`               |
| Cross-service vocabulary that doesn't track namespaces  | `Namespace("acme")`        |
| Multi-domain vocabulary scoped by stream category       | `NamespaceCategory("acme")` |
| Stable URN-style identifiers                            | Any strategy with `Urn`    |

Once a stream contains records under a given strategy, switching strategies changes the schema names produced for new records. Consumers that still need to decode the old records must keep the old mapping available, either through explicit registration or by sharing the same strategy.
