---
order: 1
---

# Schema names

<CopyMarkdownButton />

Every record carries a string identity for its type, the schema name. Readers use it to resolve which .NET type to decode into, and the [schema registry](../schema-registry/README.md) uses it as the key for storage, governance, and version management. The schema name surfaces in tooling, cross-service contracts, and the registry, so picking a strategy up front pays off later.

By default Kurrent derives the schema name from the .NET full type name. A record of type `OrderPlaced` in the `Shop.Orders` namespace produces the schema name `Shop.Orders.OrderPlaced`. Four strategies ship with the SDK, and each composes with one of four output formats.

## At a glance

| Strategy                                          | Required inputs               | Output template                 | Choose when                                                |
|---------------------------------------------------|-------------------------------|---------------------------------|------------------------------------------------------------|
| [Type name](#type-name-default) (default)         | Type                          | `{Namespace}.{TypeName}`        | Prototyping, internal systems, code-as-contract            |
| [Stream category](#stream-category)               | Type, stream                  | `{category}.{TypeName}`         | Event-sourced systems with stream-per-aggregate layout     |
| [Fixed namespace](#fixed-namespace)               | Type, namespace label         | `{label}.{TypeName}`            | Multi-tenant or environment-isolated platforms             |
| [Namespace and category](#namespace-and-category) | Type, stream, namespace label | `{label}.{category}.{TypeName}` | Multi-tenant event-sourced platforms with bounded contexts |

For most event-driven systems, **Stream category** is the recommended default. It aligns with how streams are naturally organized and keeps names concise without tying the wire contract to .NET code structure. For rapid prototyping or message-driven monoliths, **Type name** requires zero configuration. For platforms with tenant isolation as a first-class requirement, start with **Namespace and category**, because retrofitting tenant boundaries later is far more expensive than absorbing the upfront cost.

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

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.NameStrategy = SchemaNameStrategies.Category(SchemaNameOutputFormat.KebabCase));
```

The four factories on `SchemaNameStrategies` are `TypeName`, `Category`, `Namespace`, and `NamespaceCategory`. Each accepts an optional output format. The category-based strategies (`Category` and `NamespaceCategory`) derive their prefix from the stream and throw when the stream is empty.

### Type name (default)

Joins the .NET namespace with the short type name.

**Inputs**: type only.

**Output**: `{Namespace}.{TypeName}`

::: warning
Fully-qualified .NET names tie the wire contract to your code structure. Renaming a class or moving it to a different namespace renames the schema, which breaks consumers reading the old name. For anything beyond prototyping, prefer **Stream category** or another strategy that decouples the wire name from .NET identifiers.
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

### Stream category

Pulls the category from the stream name (everything before the first hyphen) and joins it with the short type name. The .NET namespace is discarded.

**Inputs**: type and a non-empty stream.

**Output**: `{category}.{TypeName}`

**Errors**: throws when the stream is empty.

**Choose when**

- Building event-sourced systems with stream-per-aggregate layout
- Organizing streams by domain category on platforms like KurrentDB
- Wanting concise, domain-aligned schema names
- Reducing coupling to .NET code structure

**Trade-offs**

| Pros                                    | Cons                                           |
|-----------------------------------------|------------------------------------------------|
| Concise, readable schema names          | Potential naming collisions across domains     |
| Stable across class refactors           | Less explicit service ownership in the name    |
| Aligns with stream-per-aggregate layout | Requires every record to have a stream segment |
| Easy to discover by domain              | Categories must be coordinated across teams    |

**Examples**

Given `Shop.Orders.OrderPlaced` on stream `order-7f3a`:

| Format      | Schema name                            |
|-------------|----------------------------------------|
| `None`      | order.OrderPlaced                      |
| `KebabCase` | order.order-placed                     |
| `SnakeCase` | order.order_placed                     |
| `Urn`       | urn:schemas-kurrent:order:order-placed |

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

### Namespace and category

Combines a fixed namespace label with the stream category, then appends the short type name.

**Inputs**: type, a non-empty stream, and a namespace label configured on the strategy.

**Output**: `{label}.{category}.{TypeName}`

**Errors**: throws when the stream is empty.

**Choose when**

- Running multi-tenant event-sourced platforms with bounded contexts per tenant
- Managing complex domain hierarchies across different environments
- Needing tenant isolation alongside domain grouping
- Implementing event-driven architectures with clear isolation requirements

**Trade-offs**

| Pros                                      | Cons                                                    |
|-------------------------------------------|---------------------------------------------------------|
| Tenant isolation plus domain organization | Most complex name structure of the four                 |
| Strong multi-tenant boundaries            | Requires coordination of both namespaces and categories |
| Clear bounded contexts per tenant         | Steeper learning curve for new team members             |
| Scales well for large platforms           | Schema resolution involves both pieces                  |

**Examples**

Given `Shop.Orders.OrderPlaced` on stream `order-7f3a` with namespace `acme`:

| Format      | Schema name                                 |
|-------------|---------------------------------------------|
| `None`      | acme.order.OrderPlaced                      |
| `KebabCase` | acme.order.order-placed                     |
| `SnakeCase` | acme.order.order_placed                     |
| `Urn`       | urn:schemas-kurrent:acme.order:order-placed |

## Overriding per type

When the generated name is not what you want, override it explicitly through the type mapper:

```csharp
opts.Schema.Mapper = MessageTypeMapper.Create(m => m
    .Map<OrderPlaced>()
    .Map<OrderShipped>("order.shipped.v1"));
```

The string passed to `Map<T>` becomes the wire-level schema name and bypasses the active strategy. See [Type mapping](./type-mapping.md) for the full mapper API.
