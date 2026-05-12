---
order: 1
---

# Schema names

<CopyMarkdownButton />

Every record carries a string identity for its type, the **schema name**. Readers use it to resolve which .NET type to decode into, and the [schema registry](../schema-registry/README.md) uses it as the key for storage, governance, and version management. The schema name is what shows up in tooling, in cross-service contracts, and in the registry, so picking a strategy up front pays off later.

By default Kurrent derives the schema name from the type itself. You can pick a different strategy or override the name per type.

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

**TypeName** keeps the .NET namespace and class name as the wire identifier. Best for internal-only events where types double as the contract, strongly-typed microservice meshes that share a code-level vocabulary, and DDD systems with stable class names. Costs are verbose names, tight coupling to code structure, and friction when refactoring a class moves the schema name with it.

**Category** uses the segment before the first hyphen in the stream name as the prefix and drops the .NET namespace. Best for event-sourced systems where streams are organized by aggregate or domain, and where type names are unique inside each category. Costs are potential collisions across domains and less explicit service ownership in the name.

**Namespace** ignores the .NET namespace and substitutes a fixed identifier you pass in. Best for multi-tenant platforms, hybrid cloud or environment-specific isolation, and cross-service vocabularies that need a stable external label independent of code structure. Costs are explicit namespace coordination and the risk of namespace proliferation as the organization grows.

**NamespaceCategory** combines a fixed namespace with the stream category. Best for multi-tenant event-sourced platforms where you need tenant isolation alongside domain grouping, and for systems with bounded contexts per tenant. Costs are higher schema complexity and the need to coordinate both namespaces and categories.

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

Casing transforms apply per identifier. Dots between namespace segments are preserved, so `Acme.Orders` stays `acme.orders` rather than collapsing into `acme-orders`. The `Urn` format wraps the kebab-cased components in a `urn:schemas-kurrent:` prefix, useful when downstream systems expect URN-style identifiers.

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

The right strategy depends on your architecture, the messages crossing service boundaries, and how schemas evolve. Start with the table below, then verify the choice against your discovery, isolation, and versioning needs.

| Architecture                                                  | Reach for                   |
|---------------------------------------------------------------|-----------------------------|
| Message-driven monolith, internal events                      | `TypeName()`                |
| Event-sourced platform with stream categories                 | `Category()`                |
| Multi-tenant or multi-environment system, code-agnostic names | `Namespace("acme")`         |
| Multi-tenant event-sourced platform with bounded contexts     | `NamespaceCategory("acme")` |
| Anything that needs URN-style identifiers downstream          | Any strategy with `Urn`     |

For most event-driven systems, `Category()` is the recommended default. It aligns with how streams are naturally organized, keeps names concise, and avoids coupling the wire contract to .NET code structure. For rapid prototyping or message-driven monoliths, `TypeName()` is zero-configuration and produces strong type safety without further setup. For platforms with tenant isolation as a first-class requirement, start with `NamespaceCategory()` even though it carries a higher upfront cost, because retrofitting tenant boundaries later is far more expensive.

The most important factor is consistency. A simple strategy applied uniformly beats a sophisticated one applied unevenly. Once a stream contains records under a given strategy, switching strategies changes the schema names produced for new records. Consumers that still need to decode the old records must keep the old mapping available, either through explicit registration or by sharing the same strategy.
