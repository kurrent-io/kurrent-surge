---
order: 2
---

# JSON

<CopyMarkdownButton />

::: tip
JSON is the default. Records appended without an explicit `DataFormat` call are encoded as JSON using `System.Text.Json`. It is also the only format that participates in server-side schema registration and validation today.
:::

## Append a JSON record

```csharp
var record = new AppendRecord {
    Stream = "orders",
    Value  = orderPlaced
};

await client.Streams.AppendStream(record);
```

The content type on the wire is `application/json`.

## Defaults

The JSON serializer ships with these settings:

| Setting                       | Default                                          |
|-------------------------------|--------------------------------------------------|
| `PropertyNamingPolicy`        | `JsonNamingPolicy.CamelCase`                     |
| `DictionaryKeyPolicy`         | `JsonNamingPolicy.CamelCase`                     |
| `PropertyNameCaseInsensitive` | `false`                                          |
| `DefaultIgnoreCondition`      | `JsonIgnoreCondition.WhenWritingNull`            |
| `UnmappedMemberHandling`      | `JsonUnmappedMemberHandling.Skip`                |
| `UnknownTypeHandling`         | `JsonUnknownTypeHandling.JsonNode`               |
| `NumberHandling`              | `JsonNumberHandling.AllowReadingFromString`      |
| TimeSpan converters           | ISO-8601 (nullable and non-nullable)             |
| NodaTime                      | TZDB via `NodaTime.Serialization.SystemTextJson` |

These settings are baked in. `JsonSchemaCodecOptions` exposes two knobs:

- `UserJsonContexts`: source-generated `JsonSerializerContext` instances for AOT (see [AOT](#aot)).
- `UseProtobufFormatter`: formats protobuf messages with the canonical protobuf JSON encoding. Defaults to on.

Runtime overrides for the naming policy, ignore condition, and converters are not exposed today. If you need them, [open an issue](https://github.com/kurrent-io/kurrent-surge/issues).

## AOT

For ahead-of-time scenarios, register a source-generated `JsonSerializerContext` so reflection is not needed at runtime:

```csharp
[JsonSerializable(typeof(OrderPlaced))]
[JsonSerializable(typeof(OrderConfirmed))]
public partial class OrdersJsonContext : JsonSerializerContext;

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Messages.Json = JsonSchemaCodecOptions.Default with {
        UserJsonContexts = [OrdersJsonContext.Default]
    });
```

Multiple contexts can be registered. Types not covered by any registered context fall back to reflection.

## Registry

JSON records participate fully in the [schema registry](../../schema-registry/README.md). With `AutoRegister` on, the first append for a type creates a schema. With `PreValidate` on, every outgoing payload is validated against the registered schema before it leaves the client.
