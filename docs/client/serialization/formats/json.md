---
order: 1
---

# JSON

<CopyMarkdownButton />

::: tip
JSON is the default serializer. Records appended without an explicit `WithDataFormat` call are encoded as JSON using `System.Text.Json`. JSON is also the only format that participates in server-side schema registration and validation today.
:::

## Append a JSON record

```csharp
var record = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .Build();

await client.Streams.AppendStream(record);
```

The content type on the wire is `application/json`.

## Tuning the serializer

The JSON serializer is configured by `JsonSchemaSerializerOptions` on the client's schema options.

| Setting                       | Default                                       |
|-------------------------------|-----------------------------------------------|
| `PropertyNamingPolicy`        | `JsonNamingPolicy.CamelCase`                  |
| `DictionaryKeyPolicy`         | `JsonNamingPolicy.CamelCase`                  |
| `PropertyNameCaseInsensitive` | `false`                                       |
| `DefaultIgnoreCondition`      | `JsonIgnoreCondition.WhenWritingNull`         |
| `UnmappedMemberHandling`      | `JsonUnmappedMemberHandling.Skip`             |
| `UnknownTypeHandling`         | `JsonUnknownTypeHandling.JsonNode`            |
| `NumberHandling`              | `JsonNumberHandling.AllowReadingFromString`   |
| TimeSpan converters           | ISO-8601 (nullable and non-nullable)          |
| NodaTime                      | Configured via `NodaTime.Serialization.SystemTextJson` (TZDB) |
| `UseProtobufFormatter`        | `true`                                        |

Pass a customized options record on the client:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Json = JsonSchemaSerializerOptions.Default with {
        UseProtobufFormatter = false
    });
```

## Type resolution

The resolver chain runs in this order:

1. The internal `KurrentJsonContext` source generator.
2. Any `JsonSerializerContext` instances supplied via `UserJsonContexts`.
3. Reflection fallback.

Apply your own converters and source-gen settings on the contexts you register. The internal context is always first in the chain, so it can be overridden by user contexts only for types it does not own.

## AOT and source generation

For ahead-of-time scenarios, register a source-generated context. Each context is added to the resolver chain alongside the internal context.

```csharp
[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderConfirmed))]
public partial class OrdersJsonContext : JsonSerializerContext;

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Json = JsonSchemaSerializerOptions.Default with {
        UserJsonContexts = [OrdersJsonContext.Default]
    });
```

Multiple contexts can be supplied. The reflection fallback handles types not covered by any registered context.

## Protobuf JSON formatter

When a value implements `Google.Protobuf.IMessage` and `UseProtobufFormatter` is enabled (the default), the JSON serializer delegates to Protobuf's canonical JSON mapping rather than vanilla `System.Text.Json` reflection. The record is still stored as JSON. The encoding differs only for protobuf-generated types.

Disable the formatter when you want straight `System.Text.Json` behavior on those types:

```csharp
opts.Schema.Json = JsonSchemaSerializerOptions.Default with {
    UseProtobufFormatter = false
};
```

## Registry integration

JSON records participate fully in the [schema registry](../../schema-registry/README.md). With `AutoRegister` on, the first append for a type creates a schema. With `PreValidate` on, every outgoing payload is validated against the registered schema before it leaves the client.
