---
order: 2
---

# Protocol Buffers

<CopyMarkdownButton />

Pick `SchemaDataFormat.Protobuf` when you already have `.proto` types and a toolchain that produces generated C# types implementing `Google.Protobuf.IMessage`. Encoding calls `ToByteArray()` on the message. Decoding uses the type's generated `Parser`. Plain POCOs are not supported.

## Append a Protobuf record

```csharp
var record = AppendRecord.New()
    .Stream("order-123")
    .Value(new OrderPlaced { OrderId = "order-123", Product = "Widget" })
    .DataFormat(SchemaDataFormat.Protobuf)
    .Build();

await client.Streams.AppendStream(record);
```

## Registry

Protobuf records are not registered or validated against the schema registry today. The `AutoRegister` and `PreValidate` flags are no-ops for Protobuf payloads. The local mapper and naming strategy still resolve the schema name on the wire, so reads decode automatically when the type is mapped.

If you need server-side schema validation, [JSON](./json.md) is the only format that supports it today.
