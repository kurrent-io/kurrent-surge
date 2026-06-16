---
title: Overview
order: 1
---

# Formats

<CopyMarkdownButton />

A data format is how the client turns your records into bytes on append and back into typed values on read. Every record carries one alongside its [schema name](../schema-names.md). The pair tells the read path which serializer to use and which .NET type to decode into.

The client ships with three serializers, registered for you automatically. There's no extension point for custom formats today.

## How formats work

When you append a record, the client uses the record's format to pick a serializer:

1. **You set the format** on the record, or let it default to JSON
2. **The client looks up the serializer** for that format
3. **The serializer encodes** your value into bytes
4. **The format and schema name** travel with the record

On read, the same format tells the client which serializer to dispatch to, and the schema name resolves the .NET type to decode into.

## Available formats

| Format                            | Content type                      | Registry          |
|-----------------------------------|-----------------------------------|-------------------|
| [JSON](./json.md)                 | `application/json`                | Server-side       |
| [Protocol Buffers](./protobuf.md) | `application/vnd.google.protobuf` | Local mapper only |
| [Raw bytes](./bytes.md)           | `application/octet-stream`        | Skipped           |

**JSON** is the default. It works without configuration and participates fully in the schema registry.

**Protocol Buffers** is for `.proto` types you already maintain. The client uses the generated message plumbing on encode and the generated parser on decode.

**Raw bytes** stores your payload exactly as you give it. The client doesn't touch the bytes, and the registry doesn't know about them.

## The default format

JSON is the default. Records appended without an explicit format are encoded as JSON by `System.Text.Json` with preconfigured options that include camelCase property names, ISO-8601 timespans, and NodaTime support. The [JSON](./json.md) page lists the full set.

The default applies whenever you append a typed value without specifying a format:

```csharp
var record = new AppendRecord {
    Stream = "orders",
    Value  = new OrderPlaced("order-7f3a", "Widget")
};

await client.Streams.AppendStream(record);
```

It also applies when you add a message directly to a request builder. Without a format argument, the message is treated as JSON.

## Override the default

Set the format on the record when you need something other than JSON:

```csharp
var record = AppendRecord.Create(message, r => {
    r.Stream     = "orders";
    r.DataFormat = SchemaDataFormat.Protobuf;
});

await client.Streams.AppendStream(record);
```

The format travels with the record. On read, the client uses it to dispatch to the matching serializer.

## Schema registry participation

The three formats behave differently when the [schema registry](../../schema-registry/README.md) is involved.

**JSON** participates fully. With auto-registration on, the first append of a previously unseen type registers a schema. With pre-validation on, every outgoing JSON payload is checked against the registered schema before it leaves the client.

**Protocol Buffers** and **raw bytes** skip the registry round trip. They still flow through the local type mapper and naming strategy, so reads decode automatically when the .NET type is mapped, but no server-side registration or validation happens. The registry flags are no-ops for these formats.

For the registry flags themselves, see [Schema Registry > Client options](../../schema-registry/client-options.md).
