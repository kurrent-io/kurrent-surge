---
title: Serialization
index: false
---

# Serialization

<CopyMarkdownButton />

`Kurrent.Client` turns .NET values into bytes on append and back into typed values on read. The default serializer is `System.Text.Json`. Protocol Buffers and raw byte passthrough are also supported. Every record carries a **schema name** (its wire-level identity) and a **data format** (how the payload bytes are encoded). Together they tell a reader which serializer to use and which .NET type to decode into.

The serializer stack and the [schema registry](../schema-registry/README.md) are separate concerns. Serialization always runs locally. The registry stores and polices schema definitions on the server, and only JSON records participate in registry validation today.

## What's pluggable

The set of supported formats is fixed (JSON, Protobuf, Bytes). What you tune lives on the client options:

| Option                     | Default                                   | What it controls                                  |
|----------------------------|-------------------------------------------|---------------------------------------------------|
| `opts.Schema.NameStrategy` | `SchemaNameStrategies.TypeName()`         | How a .NET type becomes a wire-level schema name. |
| `opts.Schema.Mapper`       | empty, auto-populated on first use        | Bidirectional map between schema names and types. |
| `opts.Schema.Json`         | `JsonSchemaSerializerOptions.Default`     | Property casing, AOT contexts, Protobuf JSON.     |
| `opts.Schema.Protobuf`     | `ProtobufSchemaSerializerOptions.Default` | AOT parser registration.                          |
| `opts.MetadataDecoder`     | `new DefaultMetadataDecoder()`            | Inbound metadata bytes to `Metadata`.             |

## In this section

- [Schema names](./schema-names.md): how a .NET type becomes a wire-level identifier, the four naming strategies, and output formats.
- [Type mapping](./type-mapping.md): the bidirectional map between schema names and .NET types, used on encode and decode.
- [Formats](./formats/README.md): JSON, Protocol Buffers, raw bytes, and Avro.
- [Decoding records](./decoding-records.md): record shape, eager decode, decode on demand.
- [Metadata](./metadata.md): record metadata shape, wire format, customizing the inbound decoder.
- [Schema evolution](./schema-evolution.md): adding fields, renaming types, versioning, migrating strategies.

For an end-to-end producer and consumer walkthrough, see [Quickstart](../quickstart.md).

## Quick example

Set the Stream category strategy on the client (the recommended default for event-driven systems) and append a record:

```csharp
public record OrderPlaced(string OrderId, string Product);

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.NameStrategy = SchemaNameStrategies.Category(SchemaNameOutputFormat.KebabCase));

var record = AppendRecord.New()
    .Stream("order-7f3a")
    .Value(new OrderPlaced("order-7f3a", "Widget"))
    .Build();

await client.Streams.AppendStream(record);
```

The record goes out as JSON with the schema name `order.order-placed`. On the read side, records arrive decoded when the .NET type has already been touched in the current process. A cold worker reading a type it has never produced will see records arrive raw until the type is registered, so map your types up front in long-lived consumers. See [Type mapping](./type-mapping.md).
