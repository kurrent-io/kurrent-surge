---
order: 1
---

# Schema names

<CopyMarkdownButton />

Every record carries a string identity for its type, the schema name. Readers use it to resolve which .NET type to decode into, and the [schema registry](../schema-registry/README.md) uses it as the key for storage, governance, and version management. The schema name surfaces in tooling, cross-service contracts, and the registry, so picking a strategy up front pays off later.

By default Kurrent derives the schema name from the .NET full type name. A record of type `OrderPlaced` in the `Shop.Orders` namespace produces the schema name `Shop.Orders.OrderPlaced`. Two strategies ship with the SDK, and each composes with one of four output formats.

## At a glance

| Strategy                                  | Required inputs       | Output template          | Choose when                                     |
|-------------------------------------------|-----------------------|--------------------------|-------------------------------------------------|
| [Type name](#type-name-default) (default) | Type                  | `{Namespace}.{TypeName}` | Prototyping, internal systems, code-as-contract |
| [Fixed namespace](#fixed-namespace)       | Type, namespace label | `{label}.{TypeName}`     | Decoupling the wire contract from code structure; multi-tenant or environment-isolated platforms |

::: tip Recommended starting points
- **Prototyping or message-driven monoliths**: **Type name** requires zero configuration.
- **Event-sourced and event-driven systems**: name records after the domain category, not the code. `Shop.Orders.OrderPlaced` couples the wire to your .NET structure — `order.order-placed` survives any refactor and reads as the domain. When a service owns one category, **Fixed namespace** with the category as the label produces exactly that shape; when it owns several, pin each name in its [catalog](./type-mapping.md).
- **Long-lived systems and cross-language consumers**: **Fixed namespace** keeps wire names stable across refactors, or pin names per type. Retrofitting stable names later is far more expensive than absorbing the upfront cost.
:::

The most important factor is consistency. A simple strategy applied uniformly beats a sophisticated one applied unevenly. For renames, version bumps, or switching strategies on a live stream, see [Schema evolution](./schema-evolution.md).

## Output formats

The output format is the second axis of every strategy. It transforms the casing of the generated name and applies the same way regardless of which strategy produced the prefix.

| Format             | Transformation                                | Example (Type name on `Shop.Orders.OrderPlaced`) |
|--------------------|-----------------------------------------------|--------------------------------------------------|
| `None` (default)   | As-written casing, dot-separated              | `Shop.Orders.OrderPlaced`                        |
| `KebabCase`        | Lowercased, type segment hyphen-separated     | `shop.orders.order-placed`                       |
| `SnakeCase`        | Lowercased, type segment underscore-separated | `shop.orders.order_placed`                       |
| `Urn`              | URN-wrapped, kebab-cased type segment         | `urn:schemas-kurrent:shop.orders:order-placed`   |

## Naming strategies

Every registration carries its own naming source — there is no client-wide strategy. A pinned name wins, then the registration's strategy, then the default convention (the full type name). Declaratively, the strategy is a type argument on the attribute:

```csharp
public sealed class AcmeNames() : NamespaceSchemaNameStrategy("acme", SchemaNameOutputFormat.KebabCase);

[MessageType<OrderPlaced, AcmeNames>]                    // acme.order-placed
public record OrderPlaced(string OrderId);

[MessageType<OrderCancelled>("order.cancelled.v1")]      // pinned: no strategy involved
public record OrderCancelled(string OrderId);
```

On a [catalog](./type-mapping.md) or the client options, the strategy is passed per mapping:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages
        .MapMessage<OrderPlaced>(SchemaNameStrategies.Namespace("acme", SchemaNameOutputFormat.KebabCase))  // acme.order-placed
        .MapMessage<OrderShipped>(SchemaNameStrategies.Namespace("acme", SchemaNameOutputFormat.KebabCase)) // acme.order-shipped
        .MapMessage<OrderCancelled>("order.cancelled.v1"));                                                 // pinned: bypasses the strategy
```

A namespace scan applies one strategy to every type it picks up — see [Message catalogs](./message-discovery.md#scan-a-namespace).

The two factories on `SchemaNameStrategies` are `TypeName` and `Namespace`. Each accepts an optional output format.

### Type name (default)

Joins the .NET namespace with the short type name.

**Inputs**: type only.

**Output**: `{Namespace}.{TypeName}`

::: warning
Fully-qualified .NET names tie the wire contract to your code structure. Renaming a class or moving it to a different namespace renames the schema, which breaks consumers reading the old name. For anything beyond prototyping, prefer **Fixed namespace** or pin names explicitly in the [catalog](./type-mapping.md) to decouple the wire name from .NET identifiers.
:::

**Choose when**

- Building strongly-typed microservice meshes that share a code-level vocabulary
- Working with internal-only systems where types double as the contract
- Practicing Domain-Driven Design with stable class names
- Prioritizing zero-configuration setup

**Trade-offs**

| Pros                                   | Cons                                          |
|----------------------------------------|-----------------------------------------------|
| Maps directly to .NET types            | Verbose names                                 |
| Clear ownership boundaries             | Tightly coupled to code structure             |
| Natural fit for strongly-typed systems | Renaming a class renames the schema           |
| Zero-configuration setup               | Requires consistent class naming across teams |

**Examples**

Given `Shop.Orders.OrderPlaced`:

| Format      | Schema name                                  |
|-------------|----------------------------------------------|
| `None`      | Shop.Orders.OrderPlaced                      |
| `KebabCase` | shop.orders.order-placed                     |
| `SnakeCase` | shop.orders.order_placed                     |
| `Urn`       | urn:schemas-kurrent:shop.orders:order-placed |

### Fixed namespace

Replaces the .NET namespace with a label you provide. The label is the same for every record going through the client.

**Inputs**: type and a namespace label configured on the strategy.

**Output**: `{label}.{TypeName}`

**Choose when**

- Building multi-tenant platforms where each tenant needs its own namespace
- Managing hybrid cloud or environment-specific schema isolation
- Establishing a cross-service vocabulary independent of code structure
- Planning ahead for organizational changes that might rename .NET assemblies

**Trade-offs**

| Pros                                            | Cons                                             |
|-------------------------------------------------|--------------------------------------------------|
| Maximum naming flexibility                      | Requires explicit namespace coordination         |
| Strong tenant or environment isolation          | One client emits exactly one namespace           |
| Decoupled from .NET code structure              | Risk of namespace proliferation as the org grows |
| Stable across services that share the namespace | Cross-tenant lookups need extra metadata         |

**Examples**

Given `Shop.Orders.OrderPlaced` with namespace `acme`:

| Format      | Schema name                           |
|-------------|---------------------------------------|
| `None`      | acme.OrderPlaced                      |
| `KebabCase` | acme.order-placed                     |
| `SnakeCase` | acme.order_placed                     |
| `Urn`       | urn:schemas-kurrent:acme:order-placed |

## Overriding per type

When the generated name is not what you want, pin it — on the attribute or on the mapping:

```csharp
[MessageType<OrderShipped>("order.shipped.v1")]
public record OrderShipped(string OrderId);
```

```csharp
opts.Messages
    .MapMessage<OrderPlaced>()
    .MapMessage<OrderShipped>("order.shipped.v1");
```

The string becomes the wire-level schema name and bypasses any strategy. See [Type mapping](./type-mapping.md) for the full catalog API.

## Version from day one

Put a version in the name before the first record is ever written:

```csharp
.Map<OrderPlaced>("order.order-placed")     // the first rename will be a breaking one
.Map<OrderPlaced>("order.order-placed.v1")  // the first rename is already priced in
```

Schema names are append-only. Once a record carries a name, readers of that record exist forever, so a name can never be reused for a different shape — it can only be retired. That makes the unversioned name the one contract you can never fix cheaply: adding `.v1` later is itself a rename, and renames are the expensive move.

One suffix at design time buys every future migration. When the shape genuinely changes, `order.order-placed.v2` slots in next to v1 and both decode side by side — see [Schema evolution](./schema-evolution.md) for the mechanics.
