---
order: 2
---

# Naming strategies

<CopyMarkdownButton />

A naming strategy decides how a .NET type becomes a schema name when no explicit name is supplied. The default uses the type's namespace and name. Override on the schema options:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.NameStrategy = SchemaNameStrategies.Category());
```

## Strategies

`SchemaNameStrategies` provides four factories. Examples below assume the type is `Acme.Orders.OrderPlaced` and the stream is `order-123`.

| Strategy                    | Output                          |
|-----------------------------|---------------------------------|
| `TypeName()` (default)      | `Acme.Orders.OrderPlaced`       |
| `Category()`                | `order.OrderPlaced`             |
| `Namespace("acme")`         | `acme.OrderPlaced`              |
| `NamespaceCategory("acme")` | `acme.order.OrderPlaced`        |

`Category` and `NamespaceCategory` derive the category from the stream name (everything before the first `-`). They throw `ArgumentException` if the stream is empty, so reach for them only when every record has a stream segment to derive from.

## Output formats

Every factory accepts a `SchemaNameOutputFormat` that transforms the generated name. Examples below all use `TypeName()`:

| Format             | Output                                            |
|--------------------|---------------------------------------------------|
| `None` (default)   | `Acme.Orders.OrderPlaced`                         |
| `KebabCase`        | `acme-orders.order-placed`                        |
| `SnakeCase`        | `acme_orders.order_placed`                        |
| `Urn`              | `urn:schemas-kurrent:acme-orders:order-placed`    |

Combine a strategy with a format:

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Namespace("acme", SchemaNameOutputFormat.KebabCase);

// Auto-mapped schema name for OrderPlaced: "acme.order-placed"
```

## Picking a strategy

| Scenario                                              | Reach for                  |
|-------------------------------------------------------|----------------------------|
| Internal-only events, want CLR identity preserved     | `TypeName()`               |
| Events grouped by stream category, type name unique   | `Category()`               |
| Cross-service vocabulary that doesn't track namespaces | `Namespace("acme")`        |
| Versioned, multi-domain event vocabulary              | `NamespaceCategory("acme")` |
| Need stable URN-style identifiers                     | Any strategy + `Urn`       |

Once a stream contains records under a given strategy, switching strategies changes the schema names produced for new records. Consumers that decode old records must keep the old mapping available either through explicit registration or by sharing the same strategy.
