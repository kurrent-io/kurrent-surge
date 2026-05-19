---
title: Formats
order: 3
index: false
---

# Formats

<CopyMarkdownButton />

Every record carries a data format that tells the client how to encode the payload on append and decode it on read. JSON is the default and works without configuration. Set a different format per record on the append builder when you need it.

## What's available

| Format                            | Content type                      | Status                                   |
|-----------------------------------|-----------------------------------|------------------------------------------|
| [JSON](./json.md)                 | `application/json`                | Default. Works with the schema registry. |
| [Protocol Buffers](./protobuf.md) | `application/vnd.google.protobuf` | Supported. Local schema mapping only.    |
| [Raw bytes](./bytes.md)           | `application/octet-stream`        | Passthrough. Not decoded on read.        |

## Pick a format

| If you...                                               | Pick             |
|---------------------------------------------------------|------------------|
| Just want something that works                          | JSON             |
| Want server-side schema registration and validation     | JSON             |
| Already have `.proto` types and a Protobuf toolchain    | Protocol Buffers |
| Hold pre-encoded payloads (compressed, encrypted, etc.) | Raw bytes        |

JSON is the only format that participates in registry validation today. Protocol Buffers and raw bytes still flow through the type mapper and naming strategy locally, so reads decode automatically when the type is mapped. They simply skip the registry round-trip. See [Schema Registry > Client options](../../schema-registry/client-options.md) for the registry flags.
