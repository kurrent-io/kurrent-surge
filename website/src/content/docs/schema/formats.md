---
title: Formats
description: What JSON, Protobuf, Avro and raw bytes each do, and where they genuinely differ.
---

A data format decides how your payload becomes bytes. Every record carries one alongside its schema
name, and the pair tells the read path which codec to use and which CLR type to decode into.

| Format | Codec | Content type | Catalog | Registry |
|---|---|---|---|---|
| `Json` | built in, **default** | `application/json` | yes | yes |
| `Protobuf` | built in | `application/vnd.google.protobuf` | yes | yes |
| `Avro` | built in | `application/vnd.apache.avro+json` | yes | yes |
| `Bytes` | none | `application/octet-stream` | **no** | **no** |

## The registry stores JSON Schema, whatever the wire format

This is the part worth reading twice. JSON, Protobuf and Avro all participate in the registry, and
all three register **the same kind of definition**: a JSON Schema, exported from your CLR type.

The wire encoding and the registered definition are separate decisions. An Avro-encoded record still
has its type exported to JSON Schema and registered under its schema name. That is what gives
compatibility rules one comparable shape to work against, no matter how the bytes were encoded.

The three encodings therefore behave alike where it matters: same catalog, same naming, same
registration, same compatibility modes. What differs is how the bytes are produced.

## JSON

The default. Records appended without an explicit format are encoded by `System.Text.Json`.

```csharp
var record = new AppendRecord {
    Stream = "order-123",
    Value  = new OrderPlaced("order-123", "Widget", 5)
};

await client.Streams.AppendStream(record);
```

Settings are fixed:

| Setting | Value |
|---|---|
| `PropertyNamingPolicy` | `CamelCase` |
| `DictionaryKeyPolicy` | `CamelCase` |
| `DefaultIgnoreCondition` | `WhenWritingNull` |
| `UnmappedMemberHandling` | `Skip` |
| `NumberHandling` | `AllowReadingFromString` |
| TimeSpan | ISO-8601 converters |
| NodaTime | TZDB |

`UnmappedMemberHandling.Skip` is what makes additive evolution safe. A reader on an older version
drops fields it does not know rather than failing.

For AOT, register a source-generated context so nothing resolves by reflection:

```csharp
[JsonSerializable(typeof(OrderPlaced))]
public partial class OrdersJsonContext : JsonSerializerContext;

opts.Messages.Json = JsonSchemaCodecOptions.Default with {
    UserJsonContexts = [OrdersJsonContext.Default]
};
```

## Protobuf

For types generated from `.proto` files. The value must implement `Google.Protobuf.IMessage`.
Encoding calls `ToByteArray()`, and decoding uses the generated `Parser`. Plain POCOs are not
supported.

```csharp
var record = AppendRecord.Create(
    new OrderPlaced { OrderId = "order-123", Product = "Widget" },
    r => {
        r.Stream     = "order-123";
        r.DataFormat = SchemaDataFormat.Protobuf;
    });

await client.Streams.AppendStream(record);
```

The codec is built in. There is no extra package and no opt-in call.

## Avro

The codec is built in — it ships inside `Kurrent.Surge.Core` with its compile-time generator. There
is no extra package and no opt-in call.

```csharp
var record = AppendRecord.Create(orderPlaced, r => {
    r.Stream     = "order-123";
    r.DataFormat = SchemaDataFormat.Avro;
});

await client.Streams.AppendStream(record);
```

Codecs are **generated at compile time**, one per message type. A type with no generated codec throws
`AvroCodecNotGeneratedException` on encode rather than falling back to reflection. The failure is
loud, and it happens on the first append rather than in production six weeks later.

The encoder is thread-static and reuses its buffer, dropping it when a single outsized message pushes
it past 64 KB so one large record cannot pin a large buffer to a thread forever.

## Raw bytes

`SchemaDataFormat.Bytes` stores your payload exactly as given. Nothing encodes, nothing decodes.

**This is the one format that is genuinely different.** A bytes record short-circuits the pipeline: it
never consults the catalog and never reaches the registry. You supply the schema name yourself.

```csharp
byte[] payload = Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.Create(payload, "my.opaque.blob", r => r.Stream = "blobs");
```

The schema name is an argument, not a settable field. These overloads set the format to `Bytes` for
you, so a raw record needs neither `DataFormat` nor a catalog entry.

Accepted input types: `byte[]`, `ReadOnlyMemory<byte>`, `Memory<byte>`, `ReadOnlySequence<byte>`,
`ArraySegment<byte>`.

On read, `Record.Data` holds the bytes, `Record.Value` stays empty and `Record.IsDecoded` is false.
There is nothing to materialise beyond the bytes.

Reach for it when the payload is already encoded in a format Kurrent Surge should not touch, when it
is compressed or encrypted and must stay opaque end to end, or when you want to own serialization
entirely.

## Choosing

| Situation | Format |
|---|---|
| Anything, by default | `Json` |
| You already maintain `.proto` contracts | `Protobuf` |
| You need compact binary and a compile-time schema | `Avro` |
| The payload must stay opaque to the SDK | `Bytes` |

The first three are interchangeable from your code's point of view. They share one catalog, one
naming scheme, and one registry behaviour. Choosing between them is about encoding size, tooling, and
which contracts you already own. `Bytes` is the outlier: it opts out of everything.
