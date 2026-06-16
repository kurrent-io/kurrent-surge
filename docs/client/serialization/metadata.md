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

var record = new AppendRecord {
    Stream   = "order-7f3a",
    Value    = orderPlaced,
    Metadata = metadata
};
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

The default decoder accepts only flat JSON objects. If you're migrating from a client that wrote metadata in a different shape, those records won't decode in this SDK without help. A custom decoder reads that shape and produces the flat dictionary consumers expect, so the rest of your code never sees the old structure.

Suppose your old records stored metadata as nested JSON groups:

```json
{
  "tracking": {
    "correlationId": "8f3a2e4d-...",
    "tenant": "acme"
  },
  "audit": {
    "occurredAt": "2026-05-14T10:00:00Z"
  }
}
```

Inherit `MetadataDecoder` and walk the JSON, lifting leaf values to top-level keys:

```csharp
public sealed class NestedMetadataDecoder : MetadataDecoder {
    public static readonly NestedMetadataDecoder Instance = new();

    protected override Metadata DecodeCore(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) {
        Metadata result = new();

        using var doc = JsonDocument.Parse(bytes);
        Flatten(result, doc.RootElement);

        return result;
    }

    static void Flatten(Metadata result, JsonElement element) {
        foreach (var property in element.EnumerateObject()) {
            switch (property.Value.ValueKind) {
                case JsonValueKind.Object:
                    Flatten(result, property.Value);
                    break;
                case JsonValueKind.String:
                    result.With(property.Name, property.Value.GetString());
                    break;
                case JsonValueKind.Number:
                    result.With(property.Name, property.Value.GetRawText());
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    result.With(property.Name, property.Value.GetBoolean());
                    break;
            }
        }
    }
}
```

A flat record decodes correctly through the same path, so the decoder handles both old and new records once installed.

The same pattern handles non-JSON metadata. Say a stream holds records appended by an older client that serialized metadata as a protobuf message. Reads against that stream surface a `MetadataDecodingException` until you install a decoder that understands the protobuf shape.

```protobuf
message TrackingMetadata {
  string correlation_id = 1;
  string tenant = 2;
  google.protobuf.Timestamp occurred_at = 3;
}
```

Point the decoder at the generated class and project its fields onto the flat dictionary:

```csharp
public sealed class ProtoMetadataDecoder : MetadataDecoder {
    public static readonly ProtoMetadataDecoder Instance = new();

    protected override Metadata DecodeCore(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) {
        var parsed = TrackingMetadata.Parser.ParseFrom(bytes.Span);

        return new Metadata()
            .With("correlationId", parsed.CorrelationId)
            .With("tenant", parsed.Tenant)
            .With("occurredAt", parsed.OccurredAt.ToDateTimeOffset());
    }
}
```

The decoder runs on every record as it streams through reads and subscriptions, so the rest of your code reads `Metadata` the same way it would for any other record:

```csharp
var records = client.Streams.ReadStream("order-7f3a")
    .Where(msg => msg.IsRecord)
    .Select(msg => msg.AsRecord);

await foreach (var record in records) {
    var correlationId = record.Metadata.GetRequiredValue<string>("correlationId");
    Console.WriteLine("Correlation: {0}", correlationId);
}
```

Inheriting `MetadataDecoder` gives you `MetadataDecodingException` wrapping, so a malformed record surfaces with the stream name and schema attached. Implementing `IMetadataDecoder` directly skips that error wrapping.

Expose the decoder as a static `Instance` and pass that to the client. The decoder sits on the hot path of every read and subscription, and the implementations above carry no per-call state, so reusing one object avoids an allocation per registration and signals that the type is safe to share:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.MetadataDecoder = NestedMetadataDecoder.Instance);
```

Keep the decode work cheap too, since it runs once per record.

## Registration

`AddKurrentClient` honors any `IMetadataDecoder` registered in DI:

```csharp
services.AddSingleton<IMetadataDecoder>(NestedMetadataDecoder.Instance);

services.AddKurrentClient("kurrentdb://localhost:2113");
```

When both DI and the options callback set a decoder, the options callback wins.
