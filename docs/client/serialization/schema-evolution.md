---
order: 6
---

# Schema evolution

<CopyMarkdownButton />

Records you append today will outlive the code that wrote them. Six months from now, a service running a newer release still has to decode records appended by an older one. Schema evolution is the small set of moves that let your record types change over time without breaking readers.

This page covers the four moves that come up most often. Each section below is self-contained, so skip straight to the one that matches your situation.

## How records keep their identity

Three ideas make the rest of the page work.

Every record carries a [schema name](./schema-names.md). That name is the string identity the database stores and the key consumers use to resolve which .NET type to decode into. It is not the same thing as the .NET class name, though by default the two are linked.

The schema registry remembers what each schema name looks like. When schema validation is enabled, the registry checks appended payloads against the registered shape, and anything that drifts beyond the configured [compatibility mode](../schema-registry/compatibility-modes.md) is rejected.

The [catalog](./type-mapping.md) is where you control the relationship between a .NET type and its schema name. Every mapping resolves its name the same way: a pinned name wins, then a naming strategy, then the default convention (the full type name). Pinning is the move this page leans on.

::: warning Contracts are not domain objects
Serialize dedicated contract records, not your aggregates. The moment a domain object doubles as the wire shape, every internal refactor becomes a contract negotiation.
:::

With those three in mind, the four moves below are short.

## When to mint a new version

Version the *meaning*, not the release. A new schema version is for semantic change — a field retyped, units switched, required data removed. Additive change stays in the current version, and [compatibility modes](../schema-registry/compatibility-modes.md) enforce that mechanically, so the rule doesn't depend on review vigilance.

| Change | Verdict |
|---|---|
| Add an optional field with a default | Stay — compatible evolution |
| Add a required field | Mint — old records can't satisfy it |
| Rename or remove a field | Mint — readers of the old shape break |
| Retype a field or change its units | Mint — same bytes, different meaning |
| Rename the .NET class only | Neither — pin the name ([Rename a .NET type](#rename-a-net-type)) |

Be paranoid about enums. A new enum value is wire-compatible but breaks every old reader that switches over it exhaustively. Prefer strings for open sets, or make every reader handle unknown values explicitly.

Teams that bump per release end up at `.v9` with nobody able to say what changed. Teams that never bump ship breaking changes inside `.v1`. The table is the middle path.

## Add a field

You want to capture one more piece of data on an existing record.

```csharp
public record OrderPlaced(
    string OrderId,
    string Product,
    int Quantity = 1);
```

What stays compatible: old consumers ignore the new field. The default [JSON serializer](./formats/json.md) is configured to skip unknown members, so a reader still on the previous version decodes everything it knows about and drops the rest. New consumers see the value when the producer set it, and the default when they read an older record that did not.

Removing a field works in reverse. Roll out the consumer change first so no reader still needs the field, then drop it from the producer.

## Rename a .NET type

Your domain language sharpened. What you first modelled as `Car` is now `Vehicle` because the team realized trucks and motorcycles belonged in the same model. The records on the stream are the same records. Only the class identifier moved.

Pin the schema name explicitly on the declaration:

```csharp
// Class was renamed from Car to Vehicle.
[MessageType<Vehicle>("vehicle.registered")]
public record Vehicle(string RegistrationId);
```

What stays compatible: the .NET type has a new name, but appended records still carry the schema name `vehicle.registered`. Consumers reading older records resolve them to the renamed class. The registry sees no change because the schema name and shape are the same.

This is a rename, not an evolution. The stored shape did not change, only the class name. When the shape itself changes, give it a new schema name and project the old shape forward in a handler pipeline ([Run two versions side by side](#run-two-versions-side-by-side)).

## Run two versions side by side

The shape change is too big for compatibility mode. You renamed a field, switched units, removed a required member, or otherwise changed the contract in a way the registry will refuse.

Give the new version its own schema name and let both live on the stream:

```csharp
[MessageType<OrderShippedV1>("order.shipped.v1")]
public record OrderShippedV1(string OrderId, string Carrier);

[MessageType<OrderShippedV2>("order.shipped.v2")]
public record OrderShippedV2(string OrderId, string Carrier, string TrackingNumber);
```

Producers append V2 from now on. The registry treats V1 and V2 as independent schemas, so no compatibility check runs between them. That is the whole point. Starting a new schema name starts fresh.

Consumers handle both types until older records age out or get projected forward:

```csharp
var records = client.Streams.ReadStream("order-7f3a", new())
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    switch (record.Value) {
        case OrderShippedV1 v1: Handle(v1); break;
        case OrderShippedV2 v2: Handle(v2); break;
    }
}
```

When both versions encode the same domain concept, project V1 into V2 the moment you read it so the rest of your handler only deals with one shape. Chain upcasters version to version — each one only knows the next:

```csharp
static OrderShippedV2 Upcast(OrderShippedV1 v1) =>
    new(v1.OrderId, v1.Carrier, TrackingNumber: "");

await foreach (var record in records) {
    var shipped = record.Value switch {
        OrderShippedV1 v1 => Upcast(v1), // v1 → v2; a future v3 adds one more hop, not one more case everywhere
        OrderShippedV2 v2 => v2,
        _                 => null
    };

    if (shipped is not null)
        Handle(shipped);
}
```

Handlers see only the latest shape. Each historical version costs one upcast function, not one more case in every consumer.

## Adopt a new naming strategy

You want new record types to use a different naming convention. Types that already have records on a stream keep their names. Renames happen forward, not backward.

Declare new types with the new strategy, and pin every type that already has records:

```csharp
public sealed class AcmeNames() : NamespaceSchemaNameStrategy("acme", SchemaNameOutputFormat.KebabCase);

[MessageType<OrderPlaced>("Shop.Orders.OrderPlaced")]    // pinned: existing records keep their name
public record OrderPlaced(string OrderId);

[MessageType<OrderShipped>("Shop.Orders.OrderShipped")]  // pinned: existing records keep their name
public record OrderShipped(string OrderId);

[MessageType<OrderRefunded, AcmeNames>]                  // new type: acme.order-refunded
public record OrderRefunded(string OrderId);
```

What stays compatible: pinned types keep their original schema names for both reads and writes, so existing streams continue to work. Types you add from now on declare the new strategy, so the new convention applies only to them. Old types stay frozen. New types use the new strategy.

If a type genuinely needs a new shape, give it a new schema name and let both live on the stream ([Run two versions side by side](#run-two-versions-side-by-side)).

## Prove it in CI

A round-trip test — serialize today's type, deserialize it back — proves nothing about last year's records. The payloads that matter are the ones already on your streams.

Commit one serialized payload per schema version and decode them against current code on every build:

```csharp
[Fact]
public void decodes_order_shipped_v1_golden_payload() {
    // Captured when v1 was the live contract — never regenerate this file.
    var payload = File.ReadAllBytes("golden/order.shipped.v1.json");

    var record = JsonSerializer.Deserialize<OrderShippedV1>(payload);

    Assert.Equal("7f3a", record!.OrderId);
}
```

The golden file is the contract. If a refactor breaks decoding of a committed payload, CI fails before replay does.

## One rule that beats all four

Consistency outlasts cleverness. A simple convention applied uniformly will carry you further than a sophisticated one applied unevenly.

When in doubt, write the migration down. Name the schemas, name the .NET types, and keep the catalog declarations versioned alongside the code that depends on it. Review a catalog change the way you review a public API change — that is what it is.
