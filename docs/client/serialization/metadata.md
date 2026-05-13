---
order: 5
---

# Metadata

<CopyMarkdownButton />

Every record carries a `Metadata` collection alongside its payload. Use it for data that should travel with the record but stay out of the payload itself. Metadata is always encoded as JSON, regardless of the payload format.

## Attaching metadata

`Metadata` is a string-keyed collection with case-insensitive keys. Values can be any common .NET type.

```csharp
var metadata = new Metadata()
    .With("correlationId", Guid.NewGuid())
    .With("tenant", "acme")
    .With("issuedAt", DateTimeOffset.UtcNow);

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

All three accessors convert from strings via `TypeDescriptor`, so numbers and Guids work even when they arrive as JSON strings. Pick the one that matches how you want to handle a missing key:

| Method                           | Behavior                                 |
|----------------------------------|------------------------------------------|
| `TryGetValue<T>(key, out value)` | Returns true on success.                 |
| `GetValueOrDefault<T>(key, def)` | Returns the typed value or `def`.        |
| `GetRequiredValue<T>(key)`       | Throws `KeyNotFoundException` if absent. |

## Wire format

Metadata travels as UTF-8 JSON regardless of the payload's data format. A Protobuf record's payload is binary, but its metadata stays JSON, so inspection and tooling behave the same across formats.

Number tokens deserialize as strings, and the typed accessors convert on demand. Guids and ISO-8601 dates are detected automatically. A few system keys (schema name, format, ID) are recognized and typed for you.

## Locking

Lock a metadata instance to prevent further changes:

```csharp
var locked = metadata.Lock();           // throws on any mutation attempt
var copy = locked.CreateUnlockedCopy(); // mutable clone
```

## Stringify

`Stringify` returns a new `Metadata` where every value is its string representation. Dates become ISO-8601, byte arrays become Base64, numbers use invariant-culture formatting. Useful for logging and observability exports.

## Customizing the decoder

Inbound bytes pass through an `IMetadataDecoder` before becoming a `Metadata` instance. The default parses JSON. Replace it for non-JSON wire formats or to enrich metadata as it's decoded:

```csharp
public sealed class MyMetadataDecoder : MetadataDecoder {
    protected override Metadata DecodeCore(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) {
        // parse bytes and return a Metadata instance
        return new Metadata();
    }
}

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.MetadataDecoder = new MyMetadataDecoder());
```

The base class wraps `DecodeCore` in error handling and attaches the schema name and format when they're missing. The decoder runs once per record, so keep it cheap.

## Registration

`AddKurrentClient` honors any `IMetadataDecoder` registered in DI:

```csharp
services.AddSingleton<IMetadataDecoder, MyMetadataDecoder>();
services.AddKurrentClient("kurrentdb://localhost:2113");
```

When both DI and the options callback set a decoder, the options callback wins.
