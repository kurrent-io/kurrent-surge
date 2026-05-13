---
order: 5
---

# Metadata

<CopyMarkdownButton />

Every record carries a `Metadata` collection alongside its payload. Use it for data that should travel with the record but stay out of the payload itself: correlation IDs, tenant tags, timestamps, anything you want available without unpacking the message.

Metadata is always JSON, so you can inspect it with any JSON tool, regardless of payload format.

## Attaching metadata

`Metadata` is a string-keyed collection with case-insensitive keys. Values can be any common .NET type.

```csharp
var metadata = new Metadata()
    .With("correlationId", Guid.NewGuid())
    .With("tenant", "acme")
    .With("occurredAt", DateTimeOffset.UtcNow);

var record = AppendRecord.New
    .ForStream("order-7f3a")
    .WithValue(orderPlaced)
    .WithMetadata(metadata)
    .Build();
```

`With` returns the same instance so calls chain. If you already hold a dictionary, pass it to `Metadata.Create` or rely on the implicit conversion.

## Reading metadata

Read a value back from metadata:

```csharp
if (record.Metadata.TryGetValue<Guid>("correlationId", out var correlationId))
    Console.WriteLine("Correlation: {0}", correlationId);
```

All three accessors handle type conversion automatically, so numbers and Guids stored as JSON strings come back as the type you ask for. Pick the one that matches how you want to handle a missing key:

| Method                           | Behavior                                 |
|----------------------------------|------------------------------------------|
| `TryGetValue<T>(key, out value)` | Returns true on success.                 |
| `GetValueOrDefault<T>(key, def)` | Returns the typed value or `def`.        |
| `GetRequiredValue<T>(key)`       | Throws `KeyNotFoundException` if absent. |

If you need every value as a string (for log output or observability exports), call `Stringify()` to get a coerced copy where dates become ISO-8601, byte arrays become Base64, and numbers use invariant-culture formatting.

## Customizing the decoder

The default decoder parses JSON, so for most projects you won't need to touch it. The common reason to plug in a custom decoder is backward compatibility: aliasing keys from an older metadata convention so consumers don't have to know about both. Wrap the default decoder with your own `IMetadataDecoder`:

```csharp
public sealed class LegacyKeyAliasDecoder(IMetadataDecoder inner) : IMetadataDecoder {
    public async ValueTask<Metadata> Decode(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) {
        var decoded = await inner.Decode(bytes, context);

        // Older records used "corrId". Alias it to the canonical key.
        if (!decoded.ContainsKey("correlationId") && decoded.TryGetValue<Guid>("corrId", out var legacy))
            decoded.With("correlationId", legacy);

        return decoded;
    }
}

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.MetadataDecoder = new LegacyKeyAliasDecoder(new DefaultMetadataDecoder()));
```

For non-JSON formats, inherit `MetadataDecoder` and override `DecodeCore`. The base class wraps your parsing in error handling. The decoder runs once per record, so keep it cheap.

## Registration

`AddKurrentClient` honors any `IMetadataDecoder` registered in DI. For decorators like `LegacyKeyAliasDecoder`, register with a factory so the inner decoder is supplied explicitly:

```csharp
services.AddSingleton<IMetadataDecoder>(_ =>
    new LegacyKeyAliasDecoder(new DefaultMetadataDecoder()));

services.AddKurrentClient("kurrentdb://localhost:2113");
```

When both DI and the options callback set a decoder, the options callback wins.
