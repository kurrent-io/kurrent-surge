---
order: 4
---

# Decoding records

<CopyMarkdownButton />

Each record returned by a read carries the raw bytes alongside the deserialized value. Decoding happens eagerly by default and can be deferred with `SkipDecoding`.

Say you have an `OrderPlaced` record mapped to its schema:

```csharp
public record OrderPlaced(string OrderId, string Item);

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.MapMessage<OrderPlaced>());
```

The snippets below build on this. See [Type mapping](./type-mapping.md) for the full story on catalogs.

## Decode eagerly

When the schema resolves to a mapped type, records arrive decoded and `IsDecoded` is true. A schema name with no mapping passes through raw — `IsDecoded` is false and the bytes stay in `Data` — unless the operation opts into strictness with `ThrowOnUnresolved`, in which case iteration throws `MessageTypeResolutionException`. A record whose schema *is* mapped but whose payload fails to decode throws `SchemaDeserializationFailedException`.

```csharp
var stream = client.Streams.ReadStream("order-123", new());

var records = stream
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    if (record.IsDecoded)
        Console.WriteLine("Type: {0}, Value: {1}", record.ValueType.Name, record.Value);
}
```

## Decode on demand

With `SkipDecoding` set on the options, records arrive with `Value` empty and `IsDecoded` false. Call `Decode` on each record to materialize the value.

```csharp{2,10}
var stream = client.Streams.ReadStream("order-123", new() {
    SkipDecoding = true
});

var records = stream
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    if (await record.Decode() == DecodeOutcome.Decoded)
        Console.WriteLine("Decoded: {0}", record.Value);
}
```

`Decode` reports an explicit `DecodeOutcome`; failures (deserialization errors, governance violations) are exceptions, so the enum covers only the non-exceptional outcomes:

| Outcome       | Meaning                                                                                        |
|---------------|------------------------------------------------------------------------------------------------|
| `Decoded`     | The value was materialized — or the record was already decoded (the call is idempotent).      |
| `NoData`      | The payload is empty; there is nothing to decode.                                              |
| `Unmapped`    | The schema name has no mapped type; the record stays raw. Read `Record.Data` directly — see [Raw bytes](./formats/bytes.md) — or map the type in a [catalog](./type-mapping.md). |
| `Unavailable` | No decoder is attached (e.g. hand-built records).                                              |

## Cast to a known type

When you know the runtime type, `record.As<T>()` casts `Value` to `T` with zero overhead for reference types. Use it after eager decoding succeeds or after `Decode` reports `Decoded`.

```csharp
var order = record.As<OrderPlaced>();
```

## Record shape

| Property    | Type                   | Description                                              |
|-------------|------------------------|----------------------------------------------------------|
| `Data`      | `ReadOnlyMemory<byte>` | The raw serialized bytes.                                |
| `Value`     | `object?`              | The deserialized message, or null if not decoded.        |
| `ValueType` | `Type`                 | The .NET type of the deserialized value.                 |
| `IsDecoded` | `bool`                 | True when `Value` is populated.                          |
| `Schema`    | `RecordSchemaInfo`     | Schema metadata: name, format, version.                  |
| `TypeName`  | `string`               | CLR type name when decoded, otherwise the wire schema name. |

## Schema metadata

`Record.Schema` exposes the wire-level schema information for the record.

| Property          | Type               | Description                                                         |
|-------------------|--------------------|---------------------------------------------------------------------|
| `SchemaName`      | `SchemaName`       | The wire schema name on the record.                                 |
| `SchemaFormat`    | `SchemaDataFormat` | Data format: `Json`, `Protobuf`, `Bytes`, or `Unspecified`.         |
| `SchemaVersionId` | `SchemaVersionId`  | Identifier of the registered schema version, or `Unset`.            |
| `HasSchemaName`   | `bool`             | True when `SchemaName` is set.                                      |
| `HasSchemaFormat` | `bool`             | True when `SchemaFormat` is not `Unspecified`.                      |
| `HasSchemaId`     | `bool`             | True when `SchemaVersionId` is set.                                 |

Dispatch on `SchemaFormat` when handling mixed-format streams, or look up the source schema in the registry. See [Schema Registry › Getting a schema](../schema-registry/getting-started/getting-a-schema.md).
