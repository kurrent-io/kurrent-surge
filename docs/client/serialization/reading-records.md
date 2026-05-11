---
order: 4
---

# Reading records

<CopyMarkdownButton />

Each `Record` returned by a read carries both the raw bytes and the deserialized value. The decoder runs eagerly when the schema name resolves to a known .NET type. When the type mapping is missing at read time, the record arrives undecoded and you can decode on demand.

## Record properties

| Property    | Type                   | Description                                              |
|-------------|------------------------|----------------------------------------------------------|
| `Data`      | `ReadOnlyMemory<byte>` | The raw serialized bytes.                                |
| `Value`     | `object?`              | The deserialized message, or null if not decoded.        |
| `ValueType` | `Type`                 | The .NET type of the deserialized value.                 |
| `IsDecoded` | `bool`                 | True when `Value` is populated.                          |
| `Schema`    | `RecordSchemaInfo`     | Schema metadata: name, format, version.                  |
| `TypeName`  | `string`               | CLR type name when decoded, otherwise the wire schema name. |

## Iterating decoded values

```csharp
await foreach (var item in stream) {
    item.Switch(
        record => {
            if (record.IsDecoded)
                Console.WriteLine("Type: {0}, Value: {1}", record.ValueType.Name, record.Value);
        },
        heartbeat => { }
    );
}
```

When every type is registered up front (see [Type mapping](./type-system/type-mapping.md)), records arrive decoded and `IsDecoded` is true.

## Decode on demand

If a record arrives undecoded because the type mapping was missing, register the type and call `TryDecode`. The call returns true on success and false otherwise.

```csharp
opts.Schema.Mapper.Map<OrderPlaced>();

await foreach (var item in stream) {
    item.Switch(
        async record => {
            if (!record.IsDecoded && await record.TryDecode())
                Console.WriteLine("Decoded: {0}", record.Value);
        },
        heartbeat => { }
    );
}
```

Records stored as raw bytes always return false from `TryDecode`. Read them directly from `Record.Data`. See [Raw bytes](./data-formats/bytes.md).

::: tip
For zero-overhead casting on a known type, use the `record.As<T>()` extension.
:::

## Schema metadata

Schema information for the record is on `Record.Schema`:

```csharp
var info = record.Schema;

Console.WriteLine("Schema name: {0}", info.SchemaName);
Console.WriteLine("Data format: {0}", info.SchemaFormat);
Console.WriteLine("Has version: {0}", info.HasSchemaId);
```

Use it to dispatch on `SchemaFormat` when handling mixed-format streams, or to look up the source schema in the registry. See [Schema Registry › Getting a schema](../schema-registry/getting-started/getting-a-schema.md).
