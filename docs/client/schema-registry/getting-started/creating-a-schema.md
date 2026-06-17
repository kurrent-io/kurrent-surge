---
order: 1
---

# Creating a schema

<CopyMarkdownButton />

`CreateSchema` registers a new schema and its first version in one call. The return value is a `SchemaVersionDescriptor` carrying the new `VersionId` and `VersionNumber`.

Pick a compatibility mode at creation time. The registry enforces it on every later version. See [Compatibility modes](../compatibility-modes.md) for the trade-offs.

## Register a schema

Create a `CreateSchemaOptions` and pass it to the client.

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

var descriptor = await client.Registry.CreateSchema(new CreateSchemaOptions {
    SchemaName        = "order-placed",
    SchemaDefinition  = schemaJson,
    DataFormat        = SchemaDataFormat.Json,
    CompatibilityMode = CompatibilityMode.Backward,
    Description       = "Order placed event",
    Tags              = new Dictionary<string, string> { ["domain"] = "orders" }
});

Console.WriteLine("Created version {0}", descriptor.VersionNumber);
```

| Property            | Type                              | Description                                                  |
|---------------------|-----------------------------------|--------------------------------------------------------------|
| `SchemaName`        | `SchemaName`                      | Unique name of the schema.                                   |
| `SchemaDefinition`  | `string`                          | The schema definition.                                       |
| `DataFormat`        | `SchemaDataFormat`                | `Json`, `Protobuf`, `Avro`, or `Bytes`.                      |
| `CompatibilityMode` | `CompatibilityMode`               | Defaults to `None`. See [Compatibility modes](../compatibility-modes.md). |
| `Description`       | `string`                          | Human-readable description.                                  |
| `Tags`              | `IDictionary<string, string>`     | Tag set associated with the schema.                          |

::: tip
For the common case of name, definition, and data format, an overload skips the request type:

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json);
```

An overload also accepts a `CompatibilityMode` directly. For tags, a description, or any other
combination, configure them inline with a delegate or pass a `CreateSchemaOptions`.
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
