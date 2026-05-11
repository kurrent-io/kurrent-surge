---
order: 1
---

# How it works

<CopyMarkdownButton />

Serialization sits between your .NET types and the bytes stored in KurrentDB. The client owns the encode and decode pipeline, picks the right serializer per record, and routes through the type mapper so reads can return strongly-typed values.

## The append pipeline

When you append a record, the client runs three steps:

1. **Resolve a schema name.** The configured [naming strategy](./type-system/naming-strategies.md) turns the .NET type into a schema name, or the type mapper returns a previously registered name.
2. **Encode the payload.** The serializer for the record's data format produces bytes. Default is JSON. Other formats are opted into with `WithDataFormat`.
3. **Send.** The bytes are stored alongside the schema name and data format on the record's schema metadata.

```csharp
var record = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .Build();

await client.Streams.AppendStream(record);
```

The same call with an explicit format:

```csharp
var record = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();
```

## The read pipeline

When you read a record, the steps reverse:

1. **Resolve a .NET type.** The schema name on the wire is looked up in the type mapper. If a mapping exists, the decoder targets that type.
2. **Decode the payload.** The serializer for the record's data format produces a .NET value.
3. **Expose the result.** The deserialized value lands on `Record.Value`. Raw bytes are still available on `Record.Data`.

When the type mapping is missing at read time, the record arrives undecoded. Register the type and call `TryDecode` on demand. See [Reading records](./reading-records.md).

## Where the pieces live

| Concern               | Where to configure                                       |
|-----------------------|----------------------------------------------------------|
| Data format           | `AppendRecord` builder, `WithDataFormat`                 |
| JSON options          | `KurrentClientOptions.Schema.Json`                       |
| Protobuf options      | `KurrentClientOptions.Schema.Protobuf`                   |
| Type mapper           | `KurrentClientOptions.Schema.Mapper`                     |
| Naming strategy       | `KurrentClientOptions.Schema.NameStrategy`               |
| Auto-register, pre-validate | `KurrentClientOptions.Schema` ([Client options](../schema-registry/client-options.md)) |

The registry module and the serialization stack are independent. Serialization turns a .NET type into bytes and a schema definition. The registry stores those definitions and polices their evolution. Either piece works without the other.

## Serializer errors

If a value cannot be encoded in the chosen format, the append surfaces the underlying serializer's exception wrapped in `SchemaSerializationFailedException`. Choosing a format with no serializer wired (currently `Avro`) throws `SchemaSerializerNotFoundException`.

## Registry interaction today

Server-side schema registration and validation only run for JSON today. Records appended in any other format are mapped locally and skip the registry round-trip even when registration or validation is enabled. See [Schema Registry › Client options](../schema-registry/client-options.md).
