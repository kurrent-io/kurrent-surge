---
order: 6
---

# Schema evolution

<CopyMarkdownButton />

Stored records outlive the code that produced them. A consumer running today must decode records that were appended six months ago by a service that may have shipped three releases since. This page covers the four moves that come up most often: adding a field, renaming a .NET type, running multiple versions of the same record, and migrating between naming strategies.

The default client configuration auto-registers a schema on the first append of each type and pre-validates every later payload against the registered version. The serializer-level changes shown below therefore have to satisfy the schema's [compatibility mode](../schema-registry/compatibility-modes.md) on the registry. Disabling auto-register or pre-validate shifts that check back to your code. See [Client options](../schema-registry/client-options.md) for the presets.

## Adding a field

A new optional field is the simplest forward-compatible change. Old consumers ignore the unknown member, and new consumers read it when present and fall back to its default when not.

`JsonSchemaSerializerOptions.Default` sets `UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip`, so old readers silently discard fields they don't recognize. Producers can append the new field immediately:

```csharp
public record OrderPlaced(
    string OrderId,
    string Product,
    int Quantity = 1);   // new field, defaulted
```

Old consumers continue to decode the record into the previous shape. New consumers see the value when the producer set it, and the default when it didn't.

Removing a field works in reverse: deploy the consumer changes first (so no reader needs the field), then drop it from the producer.

## Renaming a .NET type

When a class needs a new name in code but the wire contract must not change, pin the schema name explicitly through the mapper:

```csharp
var mapper = MessageTypeMapper.Create(m => m
    .Map<OrderShippedV2>("order.shipped.v1"));
```

The .NET type is now `OrderShippedV2`, but stored records still use `order.shipped.v1`. Consumers decoding old records resolve them to the renamed type. This is the same `Map<T>(name)` overload used to override naming-strategy output for any reason, applied here to absorb a code rename without breaking readers.

## Running multiple versions

When the shape of a record changes incompatibly (a renamed field, a different unit, a removed required member), produce a new version under a new schema name and let both versions coexist on the stream:

```csharp
public record OrderShippedV1(string OrderId, string Carrier);
public record OrderShippedV2(string OrderId, string Carrier, string TrackingNumber);

var mapper = MessageTypeMapper.Create(m => m
    .Map<OrderShippedV1>("order.shipped.v1")
    .Map<OrderShippedV2>("order.shipped.v2"));
```

Producers append `OrderShippedV2` going forward. Consumers handle both .NET types until v1 records have aged out or are projected forward into v2.

Because the two types map to distinct wire names, the registry treats them as independent schemas with no compatibility relationship. That is the point. An incompatible change between v1 and v2 would be rejected by the compatibility check on a single schema, so starting under a new name starts fresh.

Dispatch on the deserialized type:

```csharp
var request = ReadStreamRequest.New.ForStream("order-7f3a").Build();

var records = client.Streams.ReadStream(request)
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    switch (record.Value) {
        case OrderShippedV1 v1: Handle(v1); break;
        case OrderShippedV2 v2: Handle(v2); break;
    }
}
```

When both versions encode the same domain concept, prefer projecting v1 into v2 in-process so the rest of the handler only deals with the current shape.

## Migrating naming strategies

Switching strategies changes the schema names produced for new records. Old records keep the names they were appended with. Consumers that need to read both still have to know how to resolve the old names.

The safe migration path is to register the legacy names explicitly while the new strategy generates names for new types:

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Category();

opts.Schema.Mapper = MessageTypeMapper.Create(m => m
    .WithStrategy(SchemaNameStrategies.Category())
    .Map<OrderPlaced>()                            // new: "order.OrderPlaced"
    .Map<OrderPlaced>("Shop.Orders.OrderPlaced")); // legacy: "Shop.Orders.OrderPlaced"
```

Both names now map to the same .NET type. The first call sets the encode-side name (used when the type is appended). The second call adds an additional decode-side entry so old records still resolve.

The most important factor across all four moves is consistency. A simple convention applied uniformly outlasts a sophisticated one applied unevenly. When in doubt, write the migration down: name the schemas, name the .NET types, and version the mapping configuration alongside the code.
