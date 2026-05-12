---
order: 1
---

# Creating a schema

<CopyMarkdownButton />

`CreateSchema` registers a new schema and its first version in one call. The return value is a `SchemaVersionDescriptor` carrying the new `VersionId` and `VersionNumber`.

Pick a compatibility mode at creation time. The registry enforces it on every later version. See [Compatibility modes](../compatibility-modes.md) for the trade-offs.

## Register a schema

Build a `CreateSchemaRequest` and pass it to the client.

```csharp
var schemaJson = """
    {
        "type": "object",
        "properties": {
            "orderId": { "type": "string" },
            "product": { "type": "string" }
        },
        "required": ["orderId", "product"]
    }
    """;

var descriptor = await client.Registry.CreateSchema(
    CreateSchemaRequest.For("order-placed")
        .WithDefinition(schemaJson)
        .WithDataFormat(SchemaDataFormat.Json)
        .WithCompatibility(CompatibilityMode.Backward)
        .WithDescription("Order placed event")
        .WithTag("domain", "orders")
        .Build());

Console.WriteLine("Created version {0}", descriptor.VersionNumber);
```

| Method                                 | Description                                                  |
|----------------------------------------|--------------------------------------------------------------|
| `For(SchemaName)`                      | Start the request for the given schema name.                 |
| `WithDefinition(string)`               | Set the schema definition.                                   |
| `WithDataFormat(SchemaDataFormat)`     | `Json`, `Protobuf`, `Avro`, or `Bytes`.                      |
| `WithCompatibility(CompatibilityMode)` | Defaults to `None`. See [Compatibility modes](../compatibility-modes.md). |
| `WithDescription(string)`              | Human-readable description.                                  |
| `WithTag(string, string)`              | Add a single tag entry.                                      |
| `WithTags(IReadOnlyDictionary<,>)`     | Replace the full tag set.                                    |

::: tip
For the common case of name, definition, and data format, an overload skips the builder:

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json);
```

Overloads also accept a `CompatibilityMode` or a tags dictionary directly.
:::

## Pin to the returned id

`SchemaVersionDescriptor.VersionId` is the stable identifier for the newly registered version. Hold it in code that needs to bind to a specific contract, such as a consumer that refuses to decode events written against a different version.

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json);

var versionId = descriptor.VersionId;
```

`VersionNumber` is convenient for humans but is sequential per schema. The id is the durable handle.

## Errors

| Exception                       | When it fires                                       |
|---------------------------------|-----------------------------------------------------|
| `SchemaAlreadyExistsException`  | A schema with the requested name already exists.    |
| `ValidationException`           | The request itself is missing required fields.      |

To register a new version of an existing schema instead, see [Adding a schema version](./updating-a-schema/adding-a-version.md).
