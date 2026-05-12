---
title: Serialization
index: false
---

# Serialization

<CopyMarkdownButton />

`Kurrent.Client` turns .NET values into bytes on append and back into typed values on read. The default serializer is `System.Text.Json`. Protocol Buffers and raw byte passthrough are also supported. Every record carries a **schema name** (its wire-level identity) and a **data format** (how the payload bytes are encoded). Together they tell a reader which serializer to use and which .NET type to decode into.

The serializer stack and the [schema registry](../schema-registry/README.md) are separate concerns. Serialization always runs locally. The registry stores and polices schema definitions on the server, and only JSON records participate in registry validation today.

## In this section

- [Schema names](./schema-names.md): how a .NET type becomes a wire-level identifier, the four naming strategies, and output formats.
- [Type mapping](./type-mapping.md): the bidirectional map between schema names and .NET types, used on encode and decode.
- [Formats](./formats/README.md): JSON, Protocol Buffers, raw bytes, and Avro.
- [Decoding records](./decoding-records.md): record shape, eager decode, decode on demand.

For an end-to-end producer and consumer walkthrough, see [Quickstart](../quickstart.md).

## Quick example

Append a record with all defaults: JSON, the `TypeName` naming strategy, and lazy auto-mapping.

```csharp
var record = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .Build();

await client.Streams.AppendStream(record);
```

On the read side, records arrive decoded when the .NET type has already been touched in the current process. A cold worker reading a type it has never produced will see records arrive raw until the type is registered, so map your types up front in long-lived consumers. See [Type mapping](./type-mapping.md).
