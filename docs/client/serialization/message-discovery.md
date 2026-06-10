---
order: 3
---

# Message catalogs

<CopyMarkdownButton />

A client only speaks the message types you declare. There is no auto-mapping: the set of types a client can append and decode — its vocabulary — is always written down, either as attributes on your contracts or as explicit `Map` calls. Catalogs are how those declarations travel: each assembly exports its declarations as named catalogs, and clients consume them automatically.

Declare once, next to the types:

```csharp
[MessageType<OrderPlaced>("order.placed.v1")]
public record OrderPlaced(string OrderId);
```

Every client in every service that references the assembly now appends and decodes `OrderPlaced` under `order.placed.v1`. No mapper setup, no startup registration list to keep in sync — and nothing was inferred: the name is pinned right there on the contract.

## Declaring message types

Each declaration carries its own naming source. Resolution order is always the same: a pinned name wins, then a strategy, then the default convention (the full .NET type name — see [Schema names](./schema-names.md)).

### Pin a name

```csharp
[MessageType<OrderPlaced>("order.placed.v1")]
public record OrderPlaced(string OrderId);
```

The string is the wire-level schema name, permanently decoupled from the .NET identifier. Pinning is the right default for anything long-lived — see [Schema names](./schema-names.md) for why.

### Derive from a strategy

```csharp
public sealed class AcmeNames() : NamespaceSchemaNameStrategy("acme", SchemaNameOutputFormat.KebabCase);

[MessageType<OrderShipped, AcmeNames>]
public record OrderShipped(string OrderId);   // acme.order-shipped
```

The strategy choice is compile-checked: `AcmeNames` must implement `ISchemaNameStrategy` and have a parameterless constructor, or the attribute does not compile. A one-line named subclass gives the strategy's configuration a home.

### Use the default convention

```csharp
[MessageType<OrderCancelled>]
public record OrderCancelled(string OrderId);   // Acme.Orders.OrderCancelled
```

No pin, no strategy: the schema name is the full type name.

### Types you don't own

The same attributes work at assembly level, so you can declare types from a referenced package without touching them:

```csharp
[assembly: MessageType<ThirdParty.InvoiceIssued>("invoice.issued.v1")]
[assembly: MessageType<ThirdParty.PaymentSettled>]   // default-convention name
```

### Scan a namespace

When a whole namespace is messages, declare the namespace instead of each type:

```csharp
[assembly: MessageCatalog("Acme.Orders", Key = "orders")]
[assembly: MessageCatalog("Acme.Billing", Key = "billing", Strategy = typeof(AcmeNames))]
[assembly: MessageCatalog<IDomainEvent>("Acme.Audit", Key = "audit")]
```

A scan-level `Strategy` names every type in the scan; without one, each type gets the default convention. The generic form constrains the scan to types implementing or deriving from the given type — a marker interface keeps scans surgical in assemblies that mix messages with other types.

A type joins a scan when it is:

| Rule                                        | Why                                      |
|---------------------------------------------|------------------------------------------|
| A class, record, or struct under the prefix | Those are the shapes messages take       |
| Not abstract, not static                    | Only concrete types can be decoded into  |
| Not an open generic                         | A schema maps to a closed type           |
| Public or internal                          | The scan runs inside your own assembly   |
| Matching the generic constraint, when set   | Your filter, your rules                  |

::: warning Scans declare in bulk — they don't infer
A namespace scan is still a declaration: you wrote the attribute, you own what it covers. But every type it picks up gets a convention-derived name. For contracts that outlive the code, prefer per-type pins.
:::

## What the compiler builds

A source generator ships inside the `Kurrent.Client` package and runs in every project that references it — you never write any of the plumbing below. From your attributes it emits, per assembly:

```csharp
// In your assembly's root namespace:
public static class MessageCatalogs {
    public static MessageCatalog Orders  { get; }   // one property per catalog key
    public static MessageCatalog Billing { get; }

    public static IReadOnlyList<MessageCatalog> All { get; }
    public static MessageCatalog? Get(string key);   // null on miss — pattern-matchable
}
```

plus `Use*Catalog()` extension methods in the `Kurrent.Client` namespace, so IntelliSense surfaces every referenced assembly's catalogs directly on the message options:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.UseOrdersCatalog());          // one catalog
                       // .UseAcmeContractsCatalogs()   // every catalog in the assembly
```

Declarations without a `Key` (or a `Catalog` on `[MessageType]`) land in the `"Default"` catalog. The generated code references your types with plain `typeof` — compile-time metadata that survives trimming and Native AOT with zero reflection.

This makes a dedicated contracts package the natural unit of contract ownership: the package carries the types, the attributes, and therefore the catalogs. Consumers pin a package version and get exactly that contract's vocabulary.

## Selecting catalogs

Discovery always runs: every catalog the referenced assemblies export is found and gets a `Use*Catalog()` extension. A client then registers two sets — the catalogs marked **always-active**, plus the ones you select explicitly:

```csharp
opts => opts.Messages.UseOrdersCatalog();   // exactly this catalog, nothing else
```

A declared catalog (`[assembly: MessageCatalog]` / `[MessageType]`) is opt-in — name it with `UseOrdersCatalog()` / `UseCatalog(...)`, or map on the fly with `MapMessage`.

### Always-active catalogs

A catalog that must be in *every* client — foundational, process-wide contracts — declares itself always-active. On a namespace scan, set it directly on the attribute:

```csharp
[assembly: MessageCatalog("Acme.Orders", Key = "orders", AlwaysActive = true)]
```

A hand-authored catalog carries the flag itself — mark the subclass with `[CustomMessageCatalog]` so it is discovered, and pass `alwaysActive: true` to the base constructor:

```csharp
[CustomMessageCatalog]
sealed class OrdersCatalog() : MessageCatalog("orders", alwaysActive: true, c => c
    .Map<OrderPlaced>("order.placed.v1")
    .Map<OrderShipped>("order.shipped.v1"));
```

A `[CustomMessageCatalog]` class surfaces exactly like a declared catalog: `MessageCatalogs.Orders` (named after the type minus its `Catalog` suffix), `MessageCatalogs.Get("orders")`, and a generated `UseOrdersCatalog()` extension. With `alwaysActive: true` it joins every client's vocabulary with no `UseCatalog` call; drop the flag and the same self-contained catalog becomes opt-in — select it with `UseOrdersCatalog()`.

::: tip Always-active is not auto-mapping
What's automatic is *registering* the catalog; its *content* was always declared by its author. Every mapping traces back to an attribute or a `Map` call someone wrote. The client never invents a mapping on its own.
:::

## Manual catalogs

Attributes are the declarative front end; the catalog object underneath is public. Build one by hand when declarations need to be computed, shared, or kept out of the contracts assembly:

```csharp
var catalog = new MessageCatalog("orders")
    .Map<OrderPlaced>("order.placed.v1")                      // pinned
    .Map<OrderShipped>(SchemaNameStrategies.Namespace("acme")) // strategy
    .Map<OrderCancelled>();                                    // default convention

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.UseCatalog(catalog));
```

A catalog is a mutable template: clients snapshot it at construction, so changing a catalog later never affects a live client.

For one-off mappings without a catalog object, map directly on the options — these land in the client's `"Default"` catalog:

```csharp
opts.Messages.MapMessage<OrderPlaced>("order.placed.v1");
```

## Composition and conflicts

A client unions everything at construction: the default catalog, explicit `UseCatalog` calls, and always-active catalogs. Identical mappings arriving from more than one catalog union harmlessly. There are no override semantics — two catalogs that map the same schema name to different types (or the same type to different names) fail client construction with an error naming the offending catalog. A conflicting vocabulary is a configuration bug, and it surfaces at startup, not mid-stream.

## When a read meets an unmapped name

Appending a type that no catalog maps always throws — there is nothing to guess a wire name from. Reading is deliberately softer: a record whose schema name has no mapping passes through *raw* — undecoded bytes plus the schema name — so fallback handlers can treat a forward-unknown type as data instead of crashing the subscription. Strict consumers opt into failing per operation:

```csharp
var records = client.Streams.ReadStream("order-123", new() {
    ThrowOnUnresolved = true   // unmapped schema name → MessageTypeResolutionException
});
```

The same flag exists on subscribe options. See [Decoding records](./decoding-records.md) for handling raw records.

## When to stay manual

Attributes cover the common case: your types, declared next to the code that owns them. Reach for [manual mapping](./type-mapping.md) when:

- **The mapping is computed.** Names assembled from configuration or tenant context can't live in an attribute.
- **You can't touch the contracts assembly** and assembly-level attributes in your own project feel too far from the configuration they affect.
- **Tight control.** A client speaks exactly the catalogs you select plus the always-active ones — nothing is pulled in implicitly.
