---
order: 3
---

# Raw bytes

<CopyMarkdownButton />

`SchemaDataFormat.Bytes` skips serialization. The payload is written verbatim and read back as bytes. Use it when the payload is already encoded (compressed, encrypted, a custom format) or when you want to handle serialization yourself.

## Accepted input types

The append builder accepts any of the following for a bytes record:

- `byte[]`
- `ReadOnlyMemory<byte>`
- `Memory<byte>`
- `ReadOnlySequence<byte>`
- `ArraySegment<byte>`

Any other type for a `Bytes` record produces an assertion failure in debug builds.

## Append a bytes record

```csharp
byte[] payload = Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.New
    .ForStream("blobs")
    .WithValue(payload)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();

await client.Streams.AppendStream(record);
```

## Reading bytes records

::: warning
Bytes records are not decoded on read. `Record.Data` holds the raw bytes. `Record.Value` stays empty and `Record.IsDecoded` returns false. `TryDecode` always returns false for bytes records.
:::

Read the bytes off `Record.Data` and handle decoding yourself:

```csharp
await foreach (var item in stream) {
    item.Switch(
        record => {
            if (record.Schema.SchemaFormat == SchemaDataFormat.Bytes) {
                ReadOnlyMemory<byte> payload = record.Data;
                // decode however you like
            }
        },
        heartbeat => { }
    );
}
```

## Registry integration

Bytes records are not registered or validated. The registry has no definition to check against. The `AutoRegister` and `PreValidate` flags are no-ops for bytes payloads.
