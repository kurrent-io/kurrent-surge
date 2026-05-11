# Serialization

<CopyMarkdownButton />

Kurrent.Client encodes record payloads on append and decodes them on read. JSON is the default. Protocol Buffers and raw byte passthrough are also supported.

## How it works

On append:

1. The .NET type resolves to a schema name through the configured naming strategy or an explicit mapping.
2. The serializer for the record's data format encodes the payload.
3. The bytes are stored alongside the schema name and format.

On read the process reverses. The schema name resolves back to a .NET type, the matching serializer decodes the bytes, and the result is exposed on `Record.Value`.

## Data formats

Pick a format with `SchemaDataFormat`:

| Value | Content type | Description |
|-------|-------------|-------------|
| `Json` | `application/json` | JSON via System.Text.Json. Default. |
| `Protobuf` | `application/vnd.google.protobuf` | Protocol Buffers binary format. |
| `Bytes` | `application/octet-stream` | Raw bytes passthrough. |
| `Avro` | `application/vnd.apache.avro+json` | Reserved. Not implemented. |

JSON is the default and can be omitted on the builder:

```csharp
var record = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .Build();
```

For other formats, call `WithDataFormat`:

```csharp
var protoRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(protoMessage)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();

var bytesRecord = AppendRecord.New
    .ForStream("blobs")
    .WithValue(rawBytes)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();
```

If a value cannot be serialized in the chosen format, the append surfaces the underlying serializer's exception wrapped in `SchemaSerializationFailedException`.

Server-side schema registration and validation only run for JSON today. Records appended in any other format are mapped locally and skip the registry round-trip even when registration or validation is enabled. See [Schema Registry › Client options](./schema-registry/client-options.md).

## JSON

The JSON serializer is configured by `JsonSchemaSerializerOptions`:

| Setting | Default |
|---------|---------|
| PropertyNamingPolicy | camelCase |
| DictionaryKeyPolicy | camelCase |
| PropertyNameCaseInsensitive | false |
| DefaultIgnoreCondition | WhenWritingNull |
| UnmappedMemberHandling | Skip |
| UnknownTypeHandling | JsonNode |
| NumberHandling | AllowReadingFromString |
| TimeSpan converters | ISO-8601 (nullable and non-nullable) |
| NodaTime | Configured via NodaTime.Serialization.SystemTextJson (TZDB) |
| UseProtobufFormatter | true |

Type resolution chains the internal `KurrentJsonContext` source generator first, then any user-supplied `JsonSerializerContext` instances, and finally falls back to reflection. Apply your own converters or source-gen settings on the contexts you register.

When a value implements `Google.Protobuf.IMessage` and `UseProtobufFormatter` is enabled (the default), the JSON serializer delegates to Protobuf's `JsonFormatter` and `JsonParser`. The record is still stored as JSON, but the encoding follows the canonical Protobuf JSON mapping.

For AOT scenarios, plug your own source-gen context onto the JSON options. Each registered context is added to the resolver chain alongside the internal context and the reflection fallback:

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

## Protocol Buffers

Protobuf requires types generated from `.proto` files that implement `Google.Protobuf.IMessage`:

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced { OrderId = "order-123", Product = "Widget" })
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();

await client.Streams.AppendStream(record);
```

Encoding calls `ToByteArray()` on the message. Decoding uses the type's generated `Parser`.

## Raw bytes

`SchemaDataFormat.Bytes` skips serialization. Use it when the payload is already encoded or when you want to handle serialization yourself. Accepted input types: `byte[]`, `ReadOnlyMemory<byte>`, `Memory<byte>`, `ReadOnlySequence<byte>`, and `ArraySegment<byte>`.

```csharp
var payload = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.New
    .ForStream("blobs")
    .WithValue(payload)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();
```

::: warning
Records stored as raw bytes are not decoded on read. The bytes are available on `Record.Data`, but `Record.Value` stays empty and `Record.IsDecoded` returns false. `TryDecode` always returns false for byte records.
:::

## Avro

Avro is reserved in `SchemaDataFormat` but no serializer is wired today. Choosing it on append throws `SchemaSerializerNotFoundException`.

## Type mapping

`MessageTypeMapper` holds a bidirectional map between schema names stored in KurrentDB and .NET types. Encoding picks the schema name for an outgoing payload. Decoding picks the .NET type for an incoming record.

By default the client auto-maps a type the first time it appears. The schema name comes from the active naming strategy, so no upfront registration is required:

```csharp
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced { OrderId = "order-123" })
    .Build();

await client.Streams.AppendStream(record);
```

Auto-mapping is convenient but lazy. A record can only be decoded once the type has been seen at least once in the current process, or registered explicitly up front.

A `MessageTypeMapper` registered in DI is applied automatically by `AddKurrentClient`. See [Create a Client › Service provider overrides](./connect/create-a-client.md#service-provider-overrides).

## Registering types

The recommended way to populate the mapper is `MessageTypeMapper.Create`, which exposes a builder that respects the configured naming strategy:

```csharp
var mapper = MessageTypeMapper.Create(m => m
    .WithStrategy(SchemaNameStrategies.Namespace("acme"))
    .Map<OrderPlaced>()
    .Map<OrderConfirmed>()
    .Map<OrderShipped>("order.shipped.v1"));

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Mapper = mapper);
```

Inside the builder, `Map<T>()` runs the strategy to derive a schema name. Pass a string to override.

The standalone `Map<T>()` method on the schema options' mapper works differently. It always uses `typeof(T).FullName` as the schema name and ignores the configured strategy:

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Category();

opts.Schema.Mapper.Map<OrderPlaced>();              // "Acme.Orders.OrderPlaced" (FullName)
opts.Schema.Mapper.Map<OrderPlaced>("order-placed"); // "order-placed"
```

If you want the strategy applied, build the mapper through `Create` or `new MessageTypeMapper.Builder()` and assign the result. Mixing the two patterns produces inconsistent names for the same type.

## Mapper API

| Method | Behavior |
|--------|----------|
| `Map(name, type)` | Adds a mapping. Returns `MapMessageTypeResult` carrying success or `MessageTypeConflictException`. Returns success when the same mapping already exists. |
| `Map<T>()` | Maps `T` using `typeof(T).FullName`. Strategy is ignored. |
| `Map<T>(name)` | Maps `T` to the given schema name. |
| `TryMap(name, type)` | Returns true on add, false when the same mapping already exists. **Throws** `MessageTypeConflictException` when a different type already holds that name. |
| `TryMap<T>(name)` | Same semantics as `TryMap(name, typeof(T))`. |
| `TryGetMessageType(name, out type)` | Looks up the .NET type for a schema name. |
| `TryGetSchemaName(type, out name)` | Looks up the schema name for a .NET type. |
| `GetOrResolveMessageType(name)` | Returns the mapped type or throws `MessageTypeResolutionException`. |

The `Map`/`TryMap` asymmetry is intentional. `Map` returns a result so callers can choose how to react. `TryMap` raises on a real conflict but treats an idempotent re-registration as a no-op. When in doubt, prefer the builder.

## Naming strategies

A naming strategy decides how a .NET type becomes a schema name when no explicit name is supplied. The default uses the type's namespace and name. Override on the schema options:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.NameStrategy = SchemaNameStrategies.Category());
```

`SchemaNameStrategies` provides four factories. Examples assume type `Acme.Orders.OrderPlaced` and stream `order-123`:

| Strategy | Output |
|----------|--------|
| `TypeName()` (default) | `Acme.Orders.OrderPlaced` |
| `Category()` | `order.OrderPlaced` |
| `Namespace("acme")` | `acme.OrderPlaced` |
| `NamespaceCategory("acme")` | `acme.order.OrderPlaced` |

Each factory accepts a `SchemaNameOutputFormat` that transforms the generated name. Examples below all use `TypeName()`:

| Format | Output |
|--------|--------|
| `None` (default) | `Acme.Orders.OrderPlaced` |
| `KebabCase` | `acme-orders.order-placed` |
| `SnakeCase` | `acme_orders.order_placed` |
| `Urn` | `urn:schemas-kurrent:acme-orders:order-placed` |

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Namespace("acme", SchemaNameOutputFormat.KebabCase);

// Auto-mapped schema name for OrderPlaced: "acme.order-placed"
```

`Category` and `NamespaceCategory` derive the category from the stream name. They throw `ArgumentException` if the stream is empty, so use them only when every record has a stream segment to derive from.

## Reading records

Each `Record` carries both raw bytes and the deserialized value:

| Property | Type | Description |
|----------|------|-------------|
| `Data` | `ReadOnlyMemory<byte>` | The raw serialized bytes. |
| `Value` | `object?` | The deserialized message, or null if not decoded. |
| `ValueType` | `Type` | The .NET type of the deserialized value. |
| `IsDecoded` | `bool` | True when `Value` is populated. |
| `Schema` | `RecordSchemaInfo` | Schema metadata: name, format, version. |
| `TypeName` | `string` | CLR type name when decoded, otherwise the wire-level schema name. |

When the schema name resolves to a known .NET type, the record is decoded automatically:

```csharp
await foreach (var item in stream) {
    item.Switch(
        record => {
            if (record.IsDecoded)
                Console.WriteLine("Type: {0}, Value: {1}", record.ValueType.Name, record.Value);
        },
        heartbeat => { }
    );
}
```

If decoding was skipped because the type mapping was missing at read time, register the type and call `TryDecode` on demand:

```csharp
opts.Schema.Mapper.Map<OrderPlaced>();

await foreach (var item in stream) {
    item.Switch(
        async record => {
            if (!record.IsDecoded && await record.TryDecode())
                Console.WriteLine("Decoded: {0}", record.Value);
        },
        heartbeat => { }
    );
}
```

`TryDecode` returns true on success, false otherwise. Records stored as raw bytes always return false. Read them directly from `Record.Data`.

::: tip
For zero-overhead casting on a known type, use the `record.As<T>()` extension.
:::

Schema metadata is on `Record.Schema`:

```csharp
var info = record.Schema;

Console.WriteLine("Schema name: {0}", info.SchemaName);
Console.WriteLine("Data format: {0}", info.SchemaFormat);
Console.WriteLine("Has version: {0}", info.HasSchemaId);
```

## Schema registry interactions

To control how the client registers schemas with the server or validates messages against them on append, see [Schema Registry › Client options](./schema-registry/client-options.md).

Server-side registration and validation only run for JSON today. Protobuf and bytes payloads still flow through the mapper and naming strategy locally, but the schema registry is not contacted. Avro is reserved but not implemented.
