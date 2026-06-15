# Kurrent .NET SDK Examples

Each example is a runnable app demonstrating a slice of the SDK.

## Message mapping

Message types are mapped to wire-level schema names through a message catalog. These examples use
one recommended approach consistently: a hand-authored catalog with **pinned names**.

```csharp
[CustomMessageCatalog]
sealed class OrdersMessageCatalog() : MessageCatalog(
    "orders", alwaysActive: true, catalog => catalog
        .Map<OrderPlaced>("OrderPlaced")
        .Map<OrderShipped>("OrderShipped"));
```

Pinned names keep the wire contract stable and decoupled from your .NET namespaces and type names,
so refactoring code never changes a schema name. `Registry.OrderManagement` shows the same approach
with explicit versions (`order.placed.v1`).

Two examples show alternatives the SDK also supports:

- `Streams.QuickStart`: zero-config namespace scan, `[assembly: MessageCatalog("Ns.Events", AlwaysActive = true)]`, which derives fully-qualified names with no per-type mapping.
- `Processor.WordCount`: a runtime `MessageCatalog` selected per client via `UseCatalog(...)`.

The SDK additionally supports inline `opts.Messages.MapMessage<T>("name")` and custom naming
strategies (for example a short prefix via `NamespaceSchemaNameStrategy`); see the SDK documentation.

Each example's subscription filter and any registry schema name use the same pinned names the
catalog declares.
