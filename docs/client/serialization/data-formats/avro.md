---
order: 4
---

# Avro

<CopyMarkdownButton />

`SchemaDataFormat.Avro` is reserved on the protocol but no serializer is wired in the client today. Choosing Avro on append throws `SchemaSerializerNotFoundException`.

The format is included in the enum so that schema definitions stored with `Avro` content type (`application/vnd.apache.avro+json`) round-trip through the registry. The encode and decode paths are pending.

For active formats, see [JSON](./json.md), [Protocol Buffers](./protobuf.md), or [Raw bytes](./bytes.md).
