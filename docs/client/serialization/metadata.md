---
order: 5
---

# Metadata

<CopyMarkdownButton />

Every record carries a `Metadata` collection alongside its payload. Use it to attach anything you want to travel with a record but stay out of the message itself. Metadata is always encoded as JSON, regardless of the payload's format.

## Shape

`Metadata` is a string-keyed, object-valued collection with case-insensitive keys. Values can be any common .NET type. The class implements `IEnumerable<KeyValuePair<string, object?>>` and exposes typed accessors.

Attach metadata to an append:

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

`With` returns the same instance for chaining. Pass a `Dictionary<string, object?>` to `Metadata.Create` or rely on the implicit conversion when you already hold a dictionary.

## Typed reads

When pulling metadata off a record, prefer the typed accessors over casting `Record.Metadata["key"]`:

| Method                           | Behavior                                                                                       |
|----------------------------------|------------------------------------------------------------------------------------------------|
| `TryGetValue<T>(key, out value)` | Returns true on success. Converts from strings via `TypeDescriptor` so numbers and Guids work. |
| `GetValueOrDefault<T>(key, def)` | Returns the typed value or the supplied default.                                               |
| `GetRequiredValue<T>(key)`       | Returns the typed value or throws `KeyNotFoundException`.                                      |

```csharp
if (record.Metadata.TryGetValue<Guid>("correlationId", out var correlationId))
    Console.WriteLine("Correlation: {0}", correlationId);
```

## Wire format

Metadata travels as UTF-8 JSON regardless of the payload's data format. A Protobuf record's payload is binary, but its metadata stays JSON, so inspection and tooling behave the same across formats. Number tokens deserialize as strings (the typed accessors convert on demand), Guids and ISO-8601 dates are detected automatically, and a few system keys (schema name, schema format, schema ID) are recognized and typed.

## Locking and Stringify

A `Metadata` instance can be locked to make later mutation explicit:

```csharp
var locked = metadata.Lock();           // throws on any mutation attempt
var copy = locked.CreateUnlockedCopy(); // mutable clone
```

`Stringify` returns a new `Metadata` where every value is its string representation: ISO-8601 for dates, Base64 for byte arrays, invariant-culture formatting for numbers. Useful for logging and observability exports.

## Customizing the decoder

Inbound bytes pass through an `IMetadataDecoder` before becoming a `Metadata` instance. The default is `DefaultMetadataDecoder`, which parses JSON. Replace it for non-JSON wire formats or to enrich metadata at decode time:

```csharp
public sealed class MyMetadataDecoder : MetadataDecoder {
    protected override Metadata DecodeCore(ReadOnlyMemory<byte> bytes, MetadataDecoderContext context) {
        // parse `bytes` and return a Metadata instance
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
