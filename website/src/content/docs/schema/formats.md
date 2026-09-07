---
title: Data Formats
description: What JSON, Protobuf, Avro and raw bytes each do, and where they genuinely differ.
---

A data format decides how your payload becomes bytes. Every record carries one alongside its schema
name, and the pair tells the read path which codec to use and which CLR type to decode into.

| Format | Codec | Content type | Catalog | Registry |
|---|---|---|---|---|
| `Json` | built in | `application/json` | yes | yes |
| `Protobuf` | built in | `application/vnd.google.protobuf` | yes | yes |
| `Avro` | built in | `application/vnd.apache.avro+json` | yes | yes |
| `Bytes` | none | `application/octet-stream` | **no** | **no** |

## How the format is chosen

A record that states a format gets it. A record that states none takes its format from the message
type:

| The type | Format |
|---|---|
| has a generated Avro codec | `Avro` |
| is a Protocol Buffers message | `Protobuf` |
| is anything else | `Json` |

`AppendRecord.DataFormat` defaults to `SchemaDataFormat.Unspecified`, which is what "states none"
means. A stated format is never narrowed.

To append everything as JSON whatever the type, set `AppendAsJson` on the client:

```csharp
opts.Schema.AppendAsJson = true;
```

A stated format still wins over it, and it is off by default.

## The registry stores JSON Schema, whatever the wire format

JSON, Protobuf and Avro all participate in the registry, and all three register **the same kind of
definition**: a JSON Schema, exported from your CLR type.

The wire encoding and the registered definition are separate decisions. An Avro-encoded record still
has its type exported to JSON Schema and registered under its schema name. That is what gives
compatibility rules one comparable shape to work against, no matter how the bytes were encoded.

The three encodings therefore behave alike where it matters: same catalog, same naming, same
registration, same compatibility modes. What differs is how the bytes are produced.

## JSON

The fallback. A type with no generated Avro codec and no Protocol Buffers heritage encodes through
`System.Text.Json`.

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

opts.Messages.Json = new JsonSchemaCodecOptions {
    UserJsonContexts = [OrdersJsonContext.Default]
};
```

## Protobuf

For types generated from `.proto` files. The value must implement `Google.Protobuf.IMessage`.
Encoding calls `ToByteArray()`, and decoding uses the generated `Parser`. Plain POCOs are not
supported.

```csharp
var record = new AppendRecord {
    Stream = "order-123",
    Value  = new OrderPlaced { OrderId = "order-123", Product = "Widget" }
};

await client.Streams.AppendStream(record);
```

Nothing states the format. A Protocol Buffers message is recognised from its type.

The codec is built in. There is no extra package, no opt-in call, and no property to set.

## Avro

The codec is built in — it ships inside `Kurrent.Surge.Core` with its compile-time generator. There
is no extra package, no opt-in call, and no property to set.

```csharp
var record = new AppendRecord { Stream = "order-123", Value = orderPlaced };

await client.Streams.AppendStream(record);
```

A generated codec is what marks the type as Avro, so nothing states the format here either.

Codecs are **generated at compile time**, one per message type. A type with no generated codec throws
`AvroCodecNotGeneratedException` on encode rather than falling back to reflection.

The encoder is thread-static and reuses its buffer. A message that pushes the buffer past 64 KB
causes the encoder to drop it, so one large record never pins a large buffer to a thread.

## Raw bytes

`SchemaDataFormat.Bytes` stores your payload exactly as given. Nothing encodes, nothing decodes.

**This is the one format that is genuinely different.** A bytes record short-circuits the pipeline: it
never consults the catalog and never reaches the registry. You supply the schema name yourself.

```csharp
byte[] payload = Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.Create(payload, "my.opaque.blob", r => r with { Stream = "blobs" });
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
| A plain CLR record or class | `Json`, chosen for you |
| You already maintain `.proto` contracts | `Protobuf` |
| You need compact binary and a compile-time schema | `Avro` |
| The payload must stay opaque to the SDK | `Bytes` |

The first three are interchangeable from your code's point of view. They share one catalog, one
naming scheme, and one registry behaviour. Choosing between them is about encoding size, tooling, and
which contracts you already own. `Bytes` is the outlier: it opts out of everything.
