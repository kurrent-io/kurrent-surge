---
order: 4
---

# Serialization

Kurrent.Client handles serialization and deserialization of event data automatically. By default, payloads are serialized as JSON using `System.Text.Json`. The client also supports Protocol Buffers and raw byte passthrough, and provides a type mapping system that links your domain types to schema names stored in KurrentDB.

## How it works

When you append a record, the client:

1. Resolves the .NET type to a **schema name** (the event type name stored in KurrentDB).
2. Serializes the payload using the serializer for the record's **data format**.
3. Stores the serialized bytes along with the schema name and format metadata.

When you read a record, the process reverses:

1. The schema name from the record is resolved back to a .NET type.
2. The appropriate serializer deserializes the bytes into an instance of that type.
3. The deserialized object is available on the `Record.Value` property.

## Data formats

The `SchemaDataFormat` enum controls which serializer is used for a record:

| Value | Content type | Description |
|-------|-------------|-------------|
| `Json` | `application/json` | JSON via `System.Text.Json`. This is the default. |
| `Protobuf` | `application/vnd.google.protobuf` | Protocol Buffers binary format. |
| `Avro` | `application/vnd.apache.avro+json` | Apache Avro format (reserved). |
| `Bytes` | `application/octet-stream` | Raw bytes passthrough with no transformation. |
| `Unspecified` | `application/octet-stream` | No format specified. Used internally. |

Set the data format per record when appending using the `AppendRecord.New` builder:

```csharp
// JSON is the default, no need to specify
var jsonRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(orderPlaced)
    .Build();

// Protobuf
var protoRecord = AppendRecord.New
    .ForStream("orders")
    .WithValue(protoMessage)
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();

// Raw bytes
var bytesRecord = AppendRecord.New
    .ForStream("blobs")
    .WithValue(rawBytes)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();
```

## JSON serialization

JSON is the default data format. The client uses `System.Text.Json` with sensible defaults configured in `JsonSchemaSerializerOptions`:

| Setting | Default |
|---------|---------|
| `PropertyNamingPolicy` | `camelCase` |
| `DictionaryKeyPolicy` | `camelCase` |
| `PropertyNameCaseInsensitive` | `false` |
| `DefaultIgnoreCondition` | `WhenWritingNull` |
| `UnmappedMemberHandling` | `Skip` |
| `NumberHandling` | `AllowReadingFromString` |
| Enum serialization | `JsonStringEnumConverter` with camelCase |
| NodaTime | Configured via `NodaTime.Serialization.SystemTextJson` |

### Protobuf JSON interop

When a message implements `Google.Protobuf.IMessage` and `UseProtobufFormatter` is enabled (the default), the JSON serializer uses Protocol Buffers' own `JsonFormatter` and `JsonParser` for serialization and deserialization. This ensures Protobuf messages serialized as JSON follow the canonical Protobuf JSON mapping, while still being stored with `SchemaDataFormat.Json`.

## Protobuf serialization

To serialize events as Protocol Buffers binary format, your message types must implement `Google.Protobuf.IMessage` (generated from `.proto` files).

```csharp
// Assuming OrderPlaced is a generated Protobuf message type
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced { OrderId = "order-123", Product = "Widget" })
    .WithDataFormat(SchemaDataFormat.Protobuf)
    .Build();

await client.Streams.AppendStream(record);
```

The `ProtobufSchemaSerializer` uses the type's generated `Parser` to deserialize and `ToByteArray()` to serialize.

## Raw bytes

Use `SchemaDataFormat.Bytes` when you want to handle serialization yourself or store opaque binary data. The `BytesPassthroughSerializer` performs no transformation -- it accepts and returns the bytes as-is.

Supported input types: `byte[]`, `ReadOnlyMemory<byte>`, `Memory<byte>`, `ReadOnlySequence<byte>`, and `ArraySegment<byte>`.

```csharp
var payload = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"data\"}");

var record = AppendRecord.New
    .ForStream("blobs")
    .WithValue(payload)
    .WithDataFormat(SchemaDataFormat.Bytes)
    .Build();
```

::: warning
Records stored with `SchemaDataFormat.Bytes` are not automatically decoded when reading. The raw bytes are available on `Record.Data`, but `Record.Value` will not be populated and `Record.IsDecoded` will return `false`.
:::

## Message type mapping

The `MessageTypeMapper` maintains a bidirectional mapping between schema names (event type names stored in KurrentDB) and .NET types. This mapping is what allows the client to deserialize records back into strongly-typed objects.

### Auto-mapping

By default, the client automatically maps types to schema names the first time they are encountered during serialization. The schema name is derived from the .NET type using a naming strategy. This means you can append and read events without any upfront registration:

```csharp
// The client auto-maps OrderPlaced to its schema name on first use.
var record = AppendRecord.New
    .ForStream("order-123")
    .WithValue(new OrderPlaced { OrderId = "order-123" })
    .Build();

await client.Streams.AppendStream(record);
```

### Explicit mapping with MessageTypeMapper

For full control over which types are mapped and what schema names they use, configure mappings on `KurrentClientOptions.Schema.Mapper` before creating the client. The mapper is mutable, so you can register types directly:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => {
        opts.Schema.Mapper.Map<OrderPlaced>();
        opts.Schema.Mapper.Map<OrderConfirmed>();
        opts.Schema.Mapper.Map<OrderShipped>();
    });
```

`Map<T>()` (no argument) uses the type's `FullName` as the schema name. Pass a `SchemaName` to override it:

```csharp
opts.Schema.Mapper.Map<OrderPlaced>("order-placed");
opts.Schema.Mapper.Map<OrderConfirmed>("order-confirmed");
```

For a builder-style configuration, `MessageTypeMapper.Builder` provides chained registration with a configurable naming strategy:

```csharp
var mapper = new MessageTypeMapper.Builder()
    .WithStrategy(SchemaNameStrategies.Namespace("acme"))
    .Map<OrderPlaced>()
    .Map<OrderConfirmed>()
    .Build();

await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema.Mapper = mapper);
```

You can also register a `MessageTypeMapper` in DI and have `AddKurrentClient` apply it automatically. See [Configuration › Service-provider overrides](./configuration.md#service-provider-overrides).

### MessageTypeMapper.Builder API

| Method                                | Description                                                                  |
|---------------------------------------|------------------------------------------------------------------------------|
| `Map<T>(string? schemaName = null)`   | Maps a type to a schema name. Auto-generates the name if not provided.       |
| `TryMap<T>(string? schemaName = null)`| Maps a type if not already mapped. Does not throw on duplicates.             |
| `WithStrategy(ISchemaNameStrategy)`   | Sets the naming strategy used for auto-generated schema names.                |
| `Build()`                             | Returns the configured `MessageTypeMapper`.                                   |

### MessageTypeMapper API

The `MessageTypeMapper` is the underlying mapping store. Key methods:

| Method                                    | Description                                                                                  |
|-------------------------------------------|----------------------------------------------------------------------------------------------|
| `Map(SchemaName, Type)`                   | Adds a mapping. Returns a `MapMessageTypeResult` (success or `MessageTypeConflictException`).|
| `Map<T>(SchemaName)`                      | Generic overload of `Map`.                                                                   |
| `Map<T>()`                                | Maps `T` using the type's `FullName` as the schema name.                                     |
| `TryMap(SchemaName, Type)`                | Adds a mapping if the schema name is not already mapped. Returns `false` if already present. |
| `TryMap<T>(SchemaName)`                   | Generic overload of `TryMap`.                                                                |
| `TryGetMessageType(SchemaName, out Type)` | Looks up the .NET type for a schema name.                                                    |
| `TryGetSchemaName(Type, out SchemaName)`  | Looks up the schema name for a .NET type.                                                    |

## Schema name strategies

A schema name strategy determines how .NET types are converted to schema names. The default strategy uses the type's namespace and name.

Configure the strategy on the schema options:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => {
        opts.Schema.NameStrategy = SchemaNameStrategies.Category();
        opts.Schema.Mapper.Map<OrderPlaced>();
        opts.Schema.Mapper.Map<OrderConfirmed>();
    });
```

Or pre-build a mapper that owns its own strategy via `MessageTypeMapper.Builder`:

```csharp
var mapper = new MessageTypeMapper.Builder()
    .WithStrategy(SchemaNameStrategies.Category())
    .Map<OrderPlaced>()
    .Map<OrderConfirmed>()
    .Build();
```

### Built-in strategies

| Strategy | Example input | Example output |
|----------|--------------|----------------|
| `SchemaNameStrategies.TypeName()` | `Acme.Orders.OrderPlaced` | `Acme.Orders.OrderPlaced` |
| `SchemaNameStrategies.Category()` | Stream `order-123`, type `OrderPlaced` | `order.OrderPlaced` |
| `SchemaNameStrategies.Namespace("acme")` | Type `OrderPlaced` | `acme.OrderPlaced` |
| `SchemaNameStrategies.NamespaceCategory("acme")` | Stream `order-123`, type `OrderPlaced` | `acme.order.OrderPlaced` |

The default strategy is `TypeName()`, which uses `MessageSchemaNameStrategy` -- it generates `{Namespace}.{TypeName}` from the .NET type.

### Output formats

Each strategy accepts an optional `SchemaNameOutputFormat` parameter that transforms the generated name:

| Format | Example |
|--------|---------|
| `None` (default) | `Acme.Orders.OrderPlaced` |
| `KebabCase` | `acme-orders.order-placed` |
| `SnakeCase` | `acme_orders.order_placed` |
| `Urn` | `urn:schemas-kurrent:acme-orders:order-placed` |

```csharp
opts.Schema.NameStrategy = SchemaNameStrategies.Namespace("acme", SchemaNameOutputFormat.KebabCase);
opts.Schema.Mapper.Map<OrderPlaced>();

// Schema name: "acme.order-placed"
```

## Reading and deserializing events

When reading events from a stream, each `Record` carries both the raw bytes and the deserialized value:

| Property | Type | Description |
|----------|------|-------------|
| `Record.Data` | `ReadOnlyMemory<byte>` | The raw serialized bytes of the record. |
| `Record.Value` | `object?` | The deserialized message, or `null` if not yet decoded. |
| `Record.ValueType` | `Type` | The .NET type of the deserialized value. |
| `Record.IsDecoded` | `bool` | `true` if `Value` has been populated. |
| `Record.Schema` | `RecordSchemaInfo` | Schema metadata: name, format, and version. |

### Automatic decoding

Records are decoded automatically when the schema name is mapped to a known .NET type. If the type mapping exists (either through explicit registration or auto-mapping), `Record.Value` is populated with the deserialized object:

```csharp
await foreach (var item in stream) {
    item.Switch(
        record => {
            if (record.IsDecoded) {
                // Value is the deserialized domain object
                Console.WriteLine($"Type: {record.ValueType.Name}, Value: {record.Value}");
            }
        },
        heartbeat => { }
    );
}
```

### On-demand decoding with TryDecode

If a record was not automatically decoded (for example, because the type mapping was not available at read time), you can attempt decoding later:

```csharp
await foreach (var item in stream) {
    item.Switch(
        async record => {
            if (!record.IsDecoded) {
                var decoded = await record.TryDecode();
                if (decoded) {
                    Console.WriteLine($"Decoded: {record.Value}");
                }
            }
        },
        heartbeat => { }
    );
}
```

`TryDecode` returns `true` if decoding succeeded, `false` otherwise. For records stored with `SchemaDataFormat.Bytes`, `TryDecode` always returns `false` -- use `Record.Data` to access the raw bytes directly.

### Accessing schema metadata

Every record carries schema information in `Record.Schema`:

```csharp
var schemaInfo = record.Schema;

Console.WriteLine($"Schema name: {schemaInfo.SchemaName}");
Console.WriteLine($"Data format: {schemaInfo.SchemaFormat}");
Console.WriteLine($"Has version: {schemaInfo.HasSchemaId}");
```

## Schema registration options

The `SchemaRegistrationOptions` record controls how the client handles schema registration and validation. Several presets are available:

| Preset | Auto-map | Registration | Validation |
|--------|----------|-------------|------------|
| `SchemaRegistrationOptions.AutoMap` | Yes | Manual | Disabled |
| `SchemaRegistrationOptions.ManualMap` | No | Manual | Disabled |
| `SchemaRegistrationOptions.AutoRegister` | Yes | Auto | Enabled |
| `SchemaRegistrationOptions.ManualRegister` | No | Manual | Enabled |
| `SchemaRegistrationOptions.ServerPolicy` | Yes | Server policy | Enabled |

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AutoMapMessages` | `bool` | `true` | Automatically map .NET types to schema names on first encounter. |
| `RegistrationMode` | `SchemaRegistrationMode` | `Manual` | Controls whether schemas are auto-registered with the schema registry. |
| `ValidationMode` | `SchemaValidationMode` | `Disabled` | Controls whether schemas are validated against the registry. |
| `SchemaNameStrategy` | `ISchemaNameStrategy` | `MessageSchemaNameStrategy` | Strategy for generating schema names. |

## Client-level schema configuration

Set `KurrentClientOptions.Schema` to choose how the client handles schema registration and validation. Use one of the static presets, or assign a custom `KurrentClientSchemaOptions`:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema = KurrentClientSchemaOptions.Disabled);
```

The `KurrentClientSchemaOptions` presets:

| Preset                      | AutoRegister | PreValidate | Description                                       |
|-----------------------------|--------------|-------------|---------------------------------------------------|
| `FullValidation`            | `true`       | `true`      | Default. Auto-register and validate schemas.      |
| `NoAutomaticRegistration`   | `false`      | `true`      | Manual registration with validation.              |
| `NoValidation`              | `true`       | `false`     | Auto-register without validation.                 |
| `Disabled`                  | `false`      | `false`     | No registration or validation.                    |

`KurrentClientSchemaOptions` also exposes `NameStrategy`, `Mapper`, `Json`, and `Protobuf`. Use `with` to override individual properties:

```csharp
opts.Schema = KurrentClientSchemaOptions.NoAutomaticRegistration with {
    NameStrategy = SchemaNameStrategies.Namespace("acme")
};
```
