---
order: 2
---

# Type mapping

<CopyMarkdownButton />

`MessageTypeMapper` holds a bidirectional map between schema names stored in KurrentDB and .NET types. Encoding picks the schema name for an outgoing payload. Decoding picks the .NET type for an incoming record.

## Auto-mapping

By default the client auto-maps a type the first time it appears. The schema name comes from the active [naming strategy](./schema-names.md), so no upfront registration is required:

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced("order-123", "Widget"))
    .Build();

await client.Streams.AppendStream(record);
```

Auto-mapping is convenient but lazy. A record can only be decoded once the .NET type has been seen at least once in the current process, or registered explicitly up front. Workers that start cold and read events for types they have never produced themselves arrive undecoded until you map the type.

A `MessageTypeMapper` registered in DI is applied automatically by `AddKurrentClient`. See [Create a Client](../connect/create-a-client.md#service-provider-overrides).

## Register types up front

The recommended way to populate the mapper is `MessageTypeMapper.Create`, which exposes a builder that respects the configured naming strategy:

```csharp
var mapper = MessageTypeMapper.Create(m => m
    .WithStrategy(SchemaNameStrategies.Namespace("acme"))
    .Map<OrderPlaced>()
    .Map<OrderConfirmed>()
    .Map<OrderShipped>("order.shipped.v1"));

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Mapper = mapper);
```

Inside the builder, `Map<T>()` runs the strategy to derive a schema name. Pass a string to override.

The standalone `Map<T>()` method on the schema options' mapper works differently. It always uses `typeof(T).FullName` as the schema name and ignores the configured strategy:

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Category();

opts.Schema.Mapper.Map<OrderPlaced>();               // "Acme.Orders.OrderPlaced" (FullName)
opts.Schema.Mapper.Map<OrderPlaced>("order-placed"); // "order-placed"
```

If you want the strategy applied, build the mapper through `Create` or `new MessageTypeMapper.Builder()` and assign the result. Mixing the two entry points (builder `Map<T>()` that respects the strategy, and standalone mapper `Map<T>()` that uses `FullName`) produces inconsistent names for the same type.

## Mapper API

| Method                              | Behavior                                                                                              |
|-------------------------------------|-------------------------------------------------------------------------------------------------------|
| `Map(name, type)`                   | Adds a mapping. Returns `MapMessageTypeResult` carrying success or `MessageTypeConflictException`. Returns success when the same mapping already exists. |
| `Map<T>()`                          | Maps `T` using `typeof(T).FullName`. Strategy is ignored.                                             |
| `Map<T>(name)`                      | Maps `T` to the given schema name.                                                                    |
| `TryMap(name, type)`                | Returns true on add, false when the same mapping already exists. Throws `MessageTypeConflictException` when a different type already holds that name. |
| `TryMap<T>(name)`                   | Same semantics as `TryMap(name, typeof(T))`.                                                          |
| `TryGetMessageType(name, out type)` | Looks up the .NET type for a schema name.                                                             |
| `TryGetSchemaName(type, out name)`  | Looks up the schema name for a .NET type.                                                             |
| `GetOrResolveMessageType(name)`     | Returns the mapped type or throws `MessageTypeResolutionException`.                                   |

The `Map`/`TryMap` asymmetry is intentional. `Map` returns a result so callers can choose how to react. `TryMap` raises on a real conflict but treats an idempotent re-registration as a no-op. When in doubt, prefer the builder.

## Errors

| Exception                            | When it fires                                                                  |
|--------------------------------------|--------------------------------------------------------------------------------|
| `MessageTypeConflictException`       | A schema name is already mapped to a different .NET type.                      |
| `MessageTypeResolutionException`     | `GetOrResolveMessageType` cannot resolve a schema name.                        |
| `MessageTypeMapNotFoundException`    | `GetSchemaName` cannot find the type, with throw-when-missing enabled.         |
| `SchemaNameMapNotFoundException`     | `GetMessageType` cannot find the schema name, with throw-when-missing enabled. |
