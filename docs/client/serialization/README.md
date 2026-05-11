---
title: Serialization
index: false
---

# Serialization

<CopyMarkdownButton />

Kurrent.Client encodes record payloads on append and decodes them on read. JSON is the default. Protocol Buffers and raw byte passthrough are also supported. Avro is reserved but not implemented.

## In this section

- [How it works](./how-it-works.md) — the encode and decode pipeline, where each piece plugs in.
- [Data formats](./data-formats/README.md) — one page per format covering when to pick it and how it encodes.
- [Type system](./type-system/README.md) — type mapping and the naming strategies that generate schema names.
- [Reading records](./reading-records.md) — record shape, decoding behavior, schema metadata.

## Quick example

JSON is the default and works without any configuration:

```csharp
var record = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .Build();

await client.Streams.AppendStream(record);
```

For other formats, set `WithDataFormat` on the record. See [Data formats](./data-formats/README.md) for the full table and per-format pages.

For registry-side concerns (auto-register, pre-validate), see [Schema Registry › Client options](../schema-registry/client-options.md).
