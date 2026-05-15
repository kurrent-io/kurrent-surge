---
order: 4
---

# Avro

<CopyMarkdownButton />

Avro is not implemented yet. `SchemaDataFormat.Avro` is reserved in the enum, but selecting it on a record raises `SchemaSerializerNotFoundException` at append time.

If you need a schema-validated format today, use [JSON](./json.md). For compact binary encoding with strong tooling, use [Protocol Buffers](./protobuf.md).
