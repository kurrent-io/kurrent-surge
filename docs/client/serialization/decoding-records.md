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
    opts => opts.Schema.Mapper.Map<OrderPlaced>());
```

The snippets below build on this. See [Type mapping](./type-mapping.md) for the full story on the mapper.

## Decode eagerly

When the schema resolves to a registered type, records arrive decoded and `IsDecoded` is true. If the schema name does not match any registration, iteration throws `SchemaDeserializationFailedException`.

```csharp
var request = ReadStreamRequest.New()
    .Stream("order-123")
    .Build();

var stream = client.Streams.ReadStream(request);

var records = stream
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    if (record.IsDecoded)
        Console.WriteLine("Type: {0}, Value: {1}", record.ValueType.Name, record.Value);
}
```

## Decode on demand

With `SkipDecoding` set on the request, records arrive with `Value` empty and `IsDecoded` false. Call `TryDecode` on each record to materialize the value.

```csharp{3,13}
var request = ReadStreamRequest.New()
    .Stream("order-123")
    .SkipDecoding()
    .Build();

var stream = client.Streams.ReadStream(request);

var records = stream
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    if (await record.TryDecode())
        Console.WriteLine("Decoded: {0}", record.Value);
}
```

`TryDecode` behavior:

- Returns true on success and false when the record is stored as raw bytes. For raw bytes, read `Record.Data` directly. See [Raw bytes](./formats/bytes.md).
- A repeat call on an already-decoded record returns true without re-decoding.
- Accepts an optional `CancellationToken`.

## Cast to a known type

When you know the runtime type, `record.As<T>()` casts `Value` to `T` with zero overhead for reference types. Use it after eager decoding succeeds or after `TryDecode` returns true.

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
| `SchemaFormat`    | `SchemaDataFormat` | Data format: `Json`, `Protobuf`, `Avro`, `Bytes`, or `Unspecified`. |
| `SchemaVersionId` | `SchemaVersionId`  | Identifier of the registered schema version, or `Unset`.            |
| `HasSchemaName`   | `bool`             | True when `SchemaName` is set.                                      |
| `HasSchemaFormat` | `bool`             | True when `SchemaFormat` is not `Unspecified`.                      |
| `HasSchemaId`     | `bool`             | True when `SchemaVersionId` is set.                                 |

Dispatch on `SchemaFormat` when handling mixed-format streams, or look up the source schema in the registry. See [Schema Registry › Getting a schema](../schema-registry/getting-started/getting-a-schema.md).
