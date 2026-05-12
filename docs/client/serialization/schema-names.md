---
order: 1
---

# Schema names

<CopyMarkdownButton />

Every record carries a string identity for its type, the **schema name**. Readers use it to resolve which .NET type to decode into, and the [schema registry](../schema-registry/README.md) uses it as the key for storage, governance, and version management. The schema name is what shows up in tooling, in cross-service contracts, and in the registry, so picking a strategy up front pays off later.

By default Kurrent derives the schema name from the type itself. You can pick a different strategy or override the name per type.

Let's say you have a basic record type in the `Shop.Orders` namespace:

```csharp
namespace Shop.Orders;

public record OrderPlaced(string OrderId, string Product);
```

By default, Kurrent identifies this type using its .NET full name, so a record of `OrderPlaced` produces a schema name of `Shop.Orders.OrderPlaced`.

Four strategies ship with the SDK, each composable with one of four output formats: raw, kebab-case, snake_case, or URN. The detailed sections below show every strategy applied to two sample messages, expanded across all four formats.

## Choosing a strategy

The right choice depends on your architecture, the messages crossing service boundaries, and how schemas evolve.

| Architecture                                              | Strategy                       |
|-----------------------------------------------------------|--------------------------------|
| Message-driven monolith, internal events                  | Type name                      |
| Event-sourced platform with stream categories             | Stream category                |
| Multi-tenant or multi-environment system                  | Fixed namespace                |
| Multi-tenant event-sourced platform with bounded contexts | Namespace and category         |
| Anything that needs URN-style identifiers downstream      | Any strategy with `Urn` format |

For most event-driven systems, **Stream category** is the recommended default. It aligns with how streams are naturally organized, keeps names concise, and avoids coupling the wire contract to .NET code structure. For rapid prototyping or message-driven monoliths, **Type name** is zero-configuration and produces strong type safety without further setup. For platforms with tenant isolation as a first-class requirement, start with **Namespace and category** even though it carries higher upfront cost, because retrofitting tenant boundaries later is far more expensive.

The most important factor is consistency. A simple strategy applied uniformly beats a sophisticated one applied unevenly. For moves that span schema names (renames, version bumps, switching strategies on a live stream), see [Schema evolution](./schema-evolution.md).

## Setting the strategy

Set the strategy on the client options. The four factories on `SchemaNameStrategies` (`TypeName`, `Category`, `Namespace`, `NamespaceCategory`) each accept an optional output format.

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.NameStrategy = SchemaNameStrategies.Category(SchemaNameOutputFormat.KebabCase));
```

The category-based strategies (`Category` and `NamespaceCategory`) both derive their prefix from the stream and throw when the stream is empty. Use them only when every record carries a stream segment to derive from.

## Type name (default)

Generates the schema name from the .NET full type name. No configuration required.

::: warning
Fully-qualified .NET names tie the wire contract to your code structure. Renaming a class or moving it to a different namespace renames the schema, which breaks consumers reading the old name. For anything beyond prototyping, prefer **Stream category** or another strategy that decouples the wire name from .NET identifiers.
:::

### Use when

- Building strongly-typed microservice meshes that share a code-level vocabulary
- Working with internal-only systems where types double as the contract
- Practicing Domain-Driven Design with stable class names
- Prioritizing zero-configuration setup

### Trade-offs

| Pros                                     | Cons                                          |
|------------------------------------------|-----------------------------------------------|
| Maps directly to .NET types              | Verbose names                                 |
| Clear ownership boundaries               | Tightly coupled to code structure             |
| Natural fit for strongly-typed systems   | Renaming a class renames the schema           |
| Zero-configuration setup                 | Requires consistent class naming across teams |

### Examples

| Message                              | Format    | Schema name                                             |
|--------------------------------------|-----------|---------------------------------------------------------|
| `Shop.Orders.OrderPlaced`            | None      | Shop.Orders.OrderPlaced                                 |
|                                      | KebabCase | shop.orders.order-placed                                |
|                                      | SnakeCase | shop.orders.order_placed                                |
|                                      | Urn       | urn:schemas-kurrent:shop.orders:order-placed            |
| `Billing.Payments.PaymentAuthorized` | None      | Billing.Payments.PaymentAuthorized                      |
|                                      | KebabCase | billing.payments.payment-authorized                     |
|                                      | SnakeCase | billing.payments.payment_authorized                     |
|                                      | Urn       | urn:schemas-kurrent:billing.payments:payment-authorized |

## Stream category

Pulls the category from the stream name (everything before the first hyphen) and joins it with the short type name. The .NET namespace is discarded.

### Use when

- Building event-sourced systems with stream-per-aggregate layout
- Organizing streams by domain category on platforms like KurrentDB
- Wanting concise, domain-aligned schema names
- Reducing coupling to .NET code structure

### Trade-offs

| Pros                                    | Cons                                           |
|-----------------------------------------|------------------------------------------------|
| Concise, readable schema names          | Potential naming collisions across domains     |
| Stable across class refactors           | Less explicit service ownership in the name    |
| Aligns with stream-per-aggregate layout | Requires every record to have a stream segment |
| Easy to discover by domain              | Categories must be coordinated across teams    |

### Examples

| Message                              | Stream         | Format    | Schema name                                    |
|--------------------------------------|----------------|-----------|------------------------------------------------|
| `Shop.Orders.OrderPlaced`            | `order-7f3a`   | None      | order.OrderPlaced                              |
|                                      |                | KebabCase | order.order-placed                             |
|                                      |                | SnakeCase | order.order_placed                             |
|                                      |                | Urn       | urn:schemas-kurrent:order:order-placed         |
| `Billing.Payments.PaymentAuthorized` | `payment-92ab` | None      | payment.PaymentAuthorized                      |
|                                      |                | KebabCase | payment.payment-authorized                     |
|                                      |                | SnakeCase | payment.payment_authorized                     |
|                                      |                | Urn       | urn:schemas-kurrent:payment:payment-authorized |

## Fixed namespace

Replaces the .NET namespace with a label you provide. The label is the same for every record going through the client.

### Use when

- Building multi-tenant platforms where each tenant needs its own namespace
- Managing hybrid cloud or environment-specific schema isolation
- Establishing a cross-service vocabulary independent of code structure
- Planning ahead for organizational changes that might rename .NET assemblies

### Trade-offs

| Pros                                            | Cons                                             |
|-------------------------------------------------|--------------------------------------------------|
| Maximum naming flexibility                      | Requires explicit namespace coordination         |
| Strong tenant or environment isolation          | One client emits exactly one namespace           |
| Decoupled from .NET code structure              | Risk of namespace proliferation as the org grows |
| Stable across services that share the namespace | Cross-tenant lookups need extra metadata         |

### Examples

| Message                              | Namespace | Format    | Schema name                                 |
|--------------------------------------|-----------|-----------|---------------------------------------------|
| `Shop.Orders.OrderPlaced`            | `acme`    | None      | acme.OrderPlaced                            |
|                                      |           | KebabCase | acme.order-placed                           |
|                                      |           | SnakeCase | acme.order_placed                           |
|                                      |           | Urn       | urn:schemas-kurrent:acme:order-placed       |
| `Billing.Payments.PaymentAuthorized` | `acme`    | None      | acme.PaymentAuthorized                      |
|                                      |           | KebabCase | acme.payment-authorized                     |
|                                      |           | SnakeCase | acme.payment_authorized                     |
|                                      |           | Urn       | urn:schemas-kurrent:acme:payment-authorized |

## Namespace and category

Combines a fixed namespace label with the stream category, then appends the short type name. Needs both a non-empty stream and a namespace label.

### Use when

- Running multi-tenant event-sourced platforms with bounded contexts per tenant
- Managing complex domain hierarchies across different environments
- Needing tenant isolation alongside domain grouping
- Implementing event-driven architectures with clear isolation requirements

### Trade-offs

| Pros                                      | Cons                                                    |
|-------------------------------------------|---------------------------------------------------------|
| Tenant isolation plus domain organization | Most complex name structure of the four                 |
| Strong multi-tenant boundaries            | Requires coordination of both namespaces and categories |
| Clear bounded contexts per tenant         | Steeper learning curve for new team members             |
| Scales well for large platforms           | Schema resolution involves both pieces                  |

### Examples

| Message                              | Namespace | Stream         | Format    | Schema name                                         |
|--------------------------------------|-----------|----------------|-----------|-----------------------------------------------------|
| `Shop.Orders.OrderPlaced`            | `acme`    | `order-7f3a`   | None      | acme.order.OrderPlaced                              |
|                                      |           |                | KebabCase | acme.order.order-placed                             |
|                                      |           |                | SnakeCase | acme.order.order_placed                             |
|                                      |           |                | Urn       | urn:schemas-kurrent:acme.order:order-placed         |
| `Billing.Payments.PaymentAuthorized` | `acme`    | `payment-92ab` | None      | acme.payment.PaymentAuthorized                      |
|                                      |           |                | KebabCase | acme.payment.payment-authorized                     |
|                                      |           |                | SnakeCase | acme.payment.payment_authorized                     |
|                                      |           |                | Urn       | urn:schemas-kurrent:acme.payment:payment-authorized |

## Overriding the name per type

When the generated name is not what you want, override it explicitly through the type mapper:

```csharp
opts.Schema.Mapper = MessageTypeMapper.Create(m => m
    .Map<OrderPlaced>()
    .Map<OrderShipped>("order.shipped.v1"));
```

The string passed to `Map<T>` becomes the wire-level schema name and bypasses the active strategy. See [Type mapping](./type-mapping.md) for the full mapper API.
