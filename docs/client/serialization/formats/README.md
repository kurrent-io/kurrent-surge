---
title: Formats
order: 3
index: false
---

# Formats

<CopyMarkdownButton />

`SchemaDataFormat` controls how a record's payload is encoded on the wire. The format is set per record on the append builder. JSON is the default and works without configuration.

| Format     | Content type                       | Status                          |
|------------|------------------------------------|---------------------------------|
| [`Json`](./json.md)         | `application/json`                 | Default. Full registry integration. |
| [`Protobuf`](./protobuf.md) | `application/vnd.google.protobuf`  | Supported. Local mapping only.  |
| [`Bytes`](./bytes.md)       | `application/octet-stream`         | Passthrough. No decode on read. |
| [`Avro`](./avro.md)         | `application/vnd.apache.avro+json` | Reserved. Not implemented.      |

## Choosing a format

| If you...                                              | Pick                          |
|--------------------------------------------------------|-------------------------------|
| Have no specific format requirement                    | `Json`                        |
| Need server-side schema registration and validation    | `Json`                        |
| Already have `.proto` types and a Protobuf toolchain   | `Protobuf`                    |
| Hold pre-encoded payloads (compressed, encrypted, etc.) | `Bytes`                       |

JSON is the only format that participates in registry validation today. Protobuf and bytes still flow through the type mapper and naming strategy locally but skip the registry round-trip even when auto-register and pre-validate are on. See [Schema Registry › Client options](../../schema-registry/client-options.md).

## Errors

If a value cannot be serialized in the chosen format, the serializer raises `SchemaSerializationFailedException`. Choosing `Avro` raises `SchemaSerializerNotFoundException`. On the append path both reach the caller wrapped in an `ArgumentException` whose `InnerException` carries the original.

The per-format pages cover the specifics: [JSON](./json.md), [Protocol Buffers](./protobuf.md), [Raw bytes](./bytes.md), [Avro](./avro.md).
