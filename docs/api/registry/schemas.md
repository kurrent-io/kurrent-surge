---
order: 1
---

# Schema Registry

The schema registry is a centralized store for managing event schemas in KurrentDB. It lets you register schemas for your event types, enforce compatibility rules across versions, and retrieve schema definitions at runtime.

Access the registry through `client.Registry`:

```csharp
var client = new KurrentClient(options);
var registry = client.Registry;
```

All registry operations return `Result<TSuccess, TError>` types. See [Error handling](../error-handling.md) for how to work with results.

## Creating a schema

Use `CreateSchema` to register a new schema. This creates the schema and its first version:

```csharp
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

var result = await client.Registry.CreateSchema(
    schemaName: "order-placed",
    schemaDefinition: """
        {
            "type": "object",
            "properties": {
                "orderId": { "type": "string" },
                "product": { "type": "string" },
                "quantity": { "type": "integer" }
            },
            "required": ["orderId", "product", "quantity"]
        }
        """,
    dataFormat: SchemaDataFormat.Json,
    compatibilityMode: CompatibilityMode.Backward,
    description: "Schema for OrderPlaced events",
    tags: new Dictionary<string, string> {
        ["domain"] = "orders",
        ["team"] = "fulfillment"
    }
);
```

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `schemaName` | `SchemaName` | Unique name for the schema. Accepts a plain `string` via implicit conversion. |
| `schemaDefinition` | `string` | The schema definition as a string (e.g. a JSON Schema document). |
| `dataFormat` | `SchemaDataFormat` | The serialization format of the schema content. |
| `compatibilityMode` | `CompatibilityMode` | Rules for how future versions must relate to previous versions. |
| `description` | `string` | Human-readable description of the schema. |
| `tags` | `Dictionary<string, string>` | Key-value metadata for categorization and filtering. |
| `cancellationToken` | `CancellationToken` | Optional cancellation token. |

### Return value

On success, returns a `SchemaVersionDescriptor` containing:

| Property | Type | Description |
|----------|------|-------------|
| `VersionId` | `SchemaVersionId` | Unique identifier for the created schema version. |
| `VersionNumber` | `int` | The version number (1 for the first version). |

## Getting a schema

Use `GetSchema` to retrieve the schema metadata by name:

```csharp
var result = await client.Registry.GetSchema("order-placed");

result.Switch(
    schema => {
        Console.WriteLine($"Schema: {schema.SchemaName}");
        Console.WriteLine($"Format: {schema.Details.DataFormat}");
        Console.WriteLine($"Compatibility: {schema.Details.Compatibility}");
        Console.WriteLine($"Latest version: {schema.LatestSchemaVersion}");
        Console.WriteLine($"Created: {schema.CreatedAt}");
        Console.WriteLine($"Updated: {schema.UpdatedAt}");
    },
    error => Console.WriteLine($"Failed: {error}")
);
```

`GetSchema` returns the schema's metadata and details, but not the schema definition itself. To get the actual definition, use `GetSchemaVersion`.

## Getting a schema version

### Latest version

Call `GetSchemaVersion` without a version number to get the latest version:

```csharp
var result = await client.Registry.GetSchemaVersion("order-placed");

result.Switch(
    version => {
        Console.WriteLine($"Version: {version.VersionNumber}");
        Console.WriteLine($"Format: {version.DataFormat}");
        Console.WriteLine($"Definition: {version.SchemaDefinition}");
        Console.WriteLine($"Registered: {version.RegisteredAt}");
    },
    error => Console.WriteLine($"Failed: {error}")
);
```

### Specific version number

Pass the version number to get an exact version:

```csharp
var result = await client.Registry.GetSchemaVersion("order-placed", versionNumber: 2);
```

### By version ID

If you already have a `SchemaVersionId` (for example, from a `CreateSchema` result), use `GetSchemaVersionById`:

```csharp
var createResult = await client.Registry.CreateSchema(
    "order-placed", definition, SchemaDataFormat.Json,
    CompatibilityMode.Backward, "Order events", new Dictionary<string, string>()
);

// Use the version ID from the creation result
var versionResult = await createResult.MatchAsync(
    async descriptor => await client.Registry.GetSchemaVersionById(descriptor.VersionId),
    error => ValueTask.FromResult(GetSchemaVersionError.FromEx(error.AsException()))
);
```

## Deleting a schema

`DeleteSchema` removes a schema and all its versions from the registry:

```csharp
var result = await client.Registry.DeleteSchema("order-placed");

result.Switch(
    success => Console.WriteLine("Schema deleted"),
    error => Console.WriteLine($"Failed: {error}")
);
```

The return type is `Result<Success, DeleteSchemaError>`. `Success` is a zero-sized marker type indicating the operation completed.

## Data formats

The `SchemaDataFormat` enum specifies the serialization format for schema definitions:

| Value | Description | Content type |
|-------|-------------|--------------|
| `Json` | JSON Schema | `application/json` |
| `Protobuf` | Protocol Buffers | `application/vnd.google.protobuf` |
| `Avro` | Apache Avro | `application/vnd.apache.avro+json` |
| `Bytes` | Raw bytes (no schema validation) | `application/octet-stream` |

## Compatibility modes

The `CompatibilityMode` enum controls how new schema versions must relate to previous ones:

| Value | Description |
|-------|-------------|
| `None` | No compatibility checks. Any schema change is allowed. |
| `Backward` | New schemas can read data written by previous schemas. Adding optional fields is allowed; removing or renaming existing fields is not. |
| `Forward` | Previous schemas can read data written by new schemas. Removing optional fields is allowed; adding required fields is not. |
| `Full` | Both backward and forward compatible. The strictest mode -- guarantees interoperability in both directions. |

## Model reference

### Schema

Returned by `GetSchema`. Contains the schema metadata but not the definition content.

| Property | Type | Description |
|----------|------|-------------|
| `SchemaName` | `SchemaName` | The unique name of the schema. |
| `Details` | `SchemaDetails` | Schema configuration details (see below). |
| `LatestSchemaVersion` | `int` | The most recent version number. |
| `CreatedAt` | `DateTimeOffset` | When the schema was first created. |
| `UpdatedAt` | `DateTimeOffset` | When the schema was last modified. |

### SchemaDetails

Nested within `Schema`. Contains the schema's configuration.

| Property | Type | Description |
|----------|------|-------------|
| `DataFormat` | `SchemaDataFormat` | The serialization format. |
| `Compatibility` | `CompatibilityMode` | The compatibility enforcement mode. |
| `Description` | `string` | Human-readable description. |
| `Tags` | `IReadOnlyDictionary<string, string>` | Key-value metadata tags. |

### SchemaVersion

Returned by `GetSchemaVersion` and `GetSchemaVersionById`. Contains the actual schema definition.

| Property | Type | Description |
|----------|------|-------------|
| `VersionId` | `SchemaVersionId` | Unique identifier for this version. |
| `VersionNumber` | `int` | Sequential version number within the schema. |
| `SchemaDefinition` | `string` | The schema definition content. |
| `DataFormat` | `SchemaDataFormat` | The serialization format of the definition. |
| `RegisteredAt` | `DateTimeOffset` | When this version was registered. |

### SchemaVersionDescriptor

Returned by `CreateSchema`. A lightweight reference to a schema version.

| Property | Type | Description |
|----------|------|-------------|
| `VersionId` | `SchemaVersionId` | Unique identifier for the created version. |
| `VersionNumber` | `int` | The version number assigned. |

### SchemaName

A strongly-typed wrapper around a schema name string. Supports implicit conversion from `string`:

```csharp
// Explicit construction
var name = SchemaName.From("order-placed");

// Implicit from string -- works anywhere SchemaName is expected
SchemaName name = "order-placed";
```

### SchemaVersionId

A strongly-typed wrapper around a `Guid`. Supports implicit conversion from both `Guid` and `string`:

```csharp
var id = SchemaVersionId.From(someGuid);
var id = SchemaVersionId.From("550e8400-e29b-41d4-a716-446655440000");
```

## Error handling

All registry operations use the Result pattern. Each operation has a dedicated error type:

| Operation | Error type |
|-----------|------------|
| `CreateSchema` | `CreateSchemaError` |
| `GetSchema` | `GetSchemaError` |
| `GetSchemaVersion` | `GetSchemaVersionError` |
| `GetSchemaVersionById` | `GetSchemaVersionError` |
| `DeleteSchema` | `DeleteSchemaError` |

Each error type wraps an `Exception` variant. Handle errors with `Switch`:

```csharp
var result = await client.Registry.GetSchema("order-placed");

result.Switch(
    schema => Console.WriteLine($"Found: {schema.SchemaName}"),
    error => error.Switch(
        exception => Console.WriteLine($"Error: {exception.Message}")
    )
);
```

Or use `Match` to transform the result:

```csharp
var result = await client.Registry.CreateSchema(
    "order-placed", definition, SchemaDataFormat.Json,
    CompatibilityMode.Backward, "Order events", new Dictionary<string, string>()
);

var message = result.Match(
    descriptor => $"Created version {descriptor.VersionNumber} ({descriptor.VersionId})",
    error => $"Creation failed: {error}"
);
```

## Complete workflow example

A typical workflow: create a schema, retrieve it, then fetch the version details.

```csharp
using Kurrent.Client;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

var client = new KurrentClient(options);

// 1. Register a schema
var definition = """
    {
        "type": "object",
        "properties": {
            "orderId": { "type": "string" },
            "product": { "type": "string" },
            "quantity": { "type": "integer" },
            "total": { "type": "number" }
        },
        "required": ["orderId", "product", "quantity"]
    }
    """;

var createResult = await client.Registry.CreateSchema(
    schemaName: "order-placed",
    schemaDefinition: definition,
    dataFormat: SchemaDataFormat.Json,
    compatibilityMode: CompatibilityMode.Backward,
    description: "Schema for OrderPlaced events in the fulfillment domain",
    tags: new Dictionary<string, string> {
        ["domain"] = "orders",
        ["version"] = "v1"
    }
);

createResult.Switch(
    descriptor => Console.WriteLine(
        $"Created schema version {descriptor.VersionNumber} (ID: {descriptor.VersionId})"
    ),
    error => Console.WriteLine($"Failed to create schema: {error}")
);

// 2. Retrieve the schema metadata
var schemaResult = await client.Registry.GetSchema("order-placed");

schemaResult.Switch(
    schema => {
        Console.WriteLine($"Schema: {schema.SchemaName}");
        Console.WriteLine($"Format: {schema.Details.DataFormat}");
        Console.WriteLine($"Compatibility: {schema.Details.Compatibility}");
        Console.WriteLine($"Description: {schema.Details.Description}");
        Console.WriteLine($"Latest version: {schema.LatestSchemaVersion}");

        foreach (var tag in schema.Details.Tags) {
            Console.WriteLine($"  Tag: {tag.Key} = {tag.Value}");
        }
    },
    error => Console.WriteLine($"Failed to get schema: {error}")
);

// 3. Fetch the latest schema version (includes the definition)
var versionResult = await client.Registry.GetSchemaVersion("order-placed");

versionResult.Switch(
    version => {
        Console.WriteLine($"Version {version.VersionNumber}:");
        Console.WriteLine($"  Registered: {version.RegisteredAt}");
        Console.WriteLine($"  Format: {version.DataFormat}");
        Console.WriteLine($"  Definition: {version.SchemaDefinition}");
    },
    error => Console.WriteLine($"Failed to get version: {error}")
);

// 4. Clean up
var deleteResult = await client.Registry.DeleteSchema("order-placed");

deleteResult.Switch(
    _ => Console.WriteLine("Schema deleted"),
    error => Console.WriteLine($"Failed to delete: {error}")
);
```
