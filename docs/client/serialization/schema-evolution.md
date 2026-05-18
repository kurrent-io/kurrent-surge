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

The schema registry remembers what each schema name looks like. When a producer appends a record, the registry checks the payload against the registered shape. Anything that drifts beyond the configured [compatibility mode](../schema-registry/compatibility-modes.md) is rejected. The default client preset auto-registers a new schema the first time it sees a new type, then validates every later append against it.

The [mapper](./type-mapping.md) is where you control the relationship between a .NET type and its schema name. By default the mapper picks a schema name from a naming strategy, such as the full type name or a category prefix. You can override that for any type when you need to.

With those three in mind, the four moves below are short.

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

You want a new class name in your code, but the database has years of records under the old schema name and you do not want to disturb them.

Pin the schema name explicitly when you build the mapper:

```csharp
var mapper = MessageTypeMapper.Create(m => m
    .Map<OrderShippedV2>("order.shipped.v1"));
```

What stays compatible: the .NET type is now OrderShippedV2, but appended records still carry the schema name `order.shipped.v1`. Consumers reading older records resolve them to the renamed class. The registry sees no change because the schema name and shape are the same.

This is the same mapper override you would use any time you want to set a schema name explicitly. Here you are using it to absorb a code rename.

## Run two versions side by side

The shape change is too big for compatibility mode. You renamed a field, switched units, removed a required member, or otherwise changed the contract in a way the registry will refuse.

Give the new version its own schema name and let both live on the stream:

```csharp
public record OrderShippedV1(string OrderId, string Carrier);
public record OrderShippedV2(string OrderId, string Carrier, string TrackingNumber);

var mapper = MessageTypeMapper.Create(m => m
    .Map<OrderShippedV1>("order.shipped.v1")
    .Map<OrderShippedV2>("order.shipped.v2"));
```

Producers append V2 from now on. The registry treats V1 and V2 as independent schemas, so no compatibility check runs between them. That is the whole point. Starting a new schema name starts fresh.

Consumers handle both types until older records age out or get projected forward:

```csharp
var request = ReadStreamRequest.New().Stream("order-7f3a").Build();

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

When both versions encode the same domain concept, project V1 into V2 the moment you read it so the rest of your handler only deals with one shape.

## Migrate to a new naming strategy

You changed the naming strategy on a stream that already has records. New records will use the new convention. Old records keep the names they were appended with. Consumers need to resolve both.

Register the legacy name explicitly while the new strategy generates names for new types:

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Category();

opts.Schema.Mapper = MessageTypeMapper.Create(m => m
    .WithStrategy(SchemaNameStrategies.Category())
    .Map<OrderPlaced>()
    .Map<OrderPlaced>("Shop.Orders.OrderPlaced"));
```

What stays compatible: both names now resolve to OrderPlaced. The first call sets the encode-side name, used when a new record is appended. The second adds a decode-side entry so consumers can still read records appended under the old convention.

## One rule that beats all four

Consistency outlasts cleverness. A simple convention applied uniformly will carry you further than a sophisticated one applied unevenly.

When in doubt, write the migration down. Name the schemas, name the .NET types, and keep the mapper configuration versioned alongside the code that depends on it.
