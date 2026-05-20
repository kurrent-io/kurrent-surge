---
order: 4
---

# Raw bytes

<CopyMarkdownButton />

`SchemaDataFormat.Bytes` stores your payload exactly as you give it. Nothing encodes on append, and nothing decodes on read. The bytes you write are the bytes you get back.

## When to use it

- The payload is already encoded in a custom format and the client should not touch it.
- The payload is compressed or encrypted and needs to stay opaque end to end.
- You want to handle serialization yourself, outside the SDK.

## Append bytes

```csharp
byte[] payload = Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.New()
    .Stream("blobs")
    .Value(payload)
    .DataFormat(SchemaDataFormat.Bytes)
    .Build();

await client.Streams.AppendStream(record);
```

## Read bytes

::: warning
Bytes records are not decoded on read. `Record.Data` holds the raw bytes. `Record.Value` stays empty and `Record.IsDecoded` returns false. `TryDecode` always returns false for bytes records.
:::

Pull the bytes off `Record.Data` and decode them yourself:

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

## Accepted input types

Any of the following are valid for a bytes record:

- `byte[]`
- `ReadOnlyMemory<byte>`
- `Memory<byte>`
- `ReadOnlySequence<byte>`
- `ArraySegment<byte>`

Any other type produces an assertion failure in debug builds.

## Registry

Bytes records are not registered or validated. There is no schema for the server to check against, so the `AutoRegister` and `PreValidate` flags are no-ops for bytes payloads.
