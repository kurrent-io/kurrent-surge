---
order: 6
---

# Schema Registry

Register, retrieve, list, and delete schemas through `client.Registry`. Each schema has a name, a definition, a serialization format, and one or more versions. Compatibility rules are enforced when new versions are registered (see [Schema compatibility](./schema-compatibility.md)).

Registry operations return values directly and throw on failure. See [Error handling](./error-handling.md) for the exception catalogue and patterns.

## Creating a schema

Use `CreateSchema` to register a new schema. This creates the schema and its first version:

```csharp
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;

var descriptor = await client.Registry.CreateSchema(
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

Console.WriteLine($"Created version {descriptor.VersionNumber} ({descriptor.VersionId})");
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `schemaName` | `SchemaName` | yes | Unique name for the schema. Accepts a plain `string` via implicit conversion. |
| `schemaDefinition` | `string` | yes | The schema definition as a string (for example, a JSON Schema document). |
| `dataFormat` | `SchemaDataFormat` | yes | The serialization format of the schema content. |
| `compatibilityMode` | `CompatibilityMode` | no, defaults to `None` | Rules for how future versions must relate to previous versions. |
| `description` | `string` | no, defaults to `""` | Human-readable description of the schema. |
| `tags` | `IReadOnlyDictionary<string, string>?` | no, defaults to `null` | Key-value metadata for categorization and filtering. |
| `cancellationToken` | `CancellationToken` | no | Optional cancellation token. |

### Return value

Returns a `SchemaVersionDescriptor`:

| Property | Type | Description |
|----------|------|-------------|
| `VersionId` | `SchemaVersionId` | Unique identifier for the created schema version. |
| `VersionNumber` | `int` | The version number (1 for the first version). |

### Errors

| Exception | When it is thrown |
|-----------|------------------|
| `SchemaAlreadyExistsException` | A schema with the same name already exists. |
| `ArgumentException` | `schemaName` is empty or `schemaDefinition` is null/whitespace. |

## Getting a schema

Use `GetSchema` to retrieve the schema metadata by name:

```csharp
var schema = await client.Registry.GetSchema("order-placed");

Console.WriteLine($"Schema: {schema.SchemaName}");
Console.WriteLine($"Format: {schema.Details.DataFormat}");
Console.WriteLine($"Compatibility: {schema.Details.Compatibility}");
Console.WriteLine($"Latest version: {schema.LatestSchemaVersion}");
Console.WriteLine($"Created: {schema.CreatedAt}");
Console.WriteLine($"Updated: {schema.UpdatedAt}");
```

`GetSchema` returns the schema's metadata and details, but not the schema definition itself. To get the actual definition, use `GetSchemaVersion`.

### Errors

| Exception | When it is thrown |
|-----------|------------------|
| `SchemaNotFoundException` | No schema with that name exists. |
| `SchemaDeletedException` | The schema was deleted. |

## Getting a schema version

### Latest version

Call `GetSchemaVersion` without a version number to get the latest version:

```csharp
var version = await client.Registry.GetSchemaVersion("order-placed");

Console.WriteLine($"Version: {version.VersionNumber}");
Console.WriteLine($"Format: {version.DataFormat}");
Console.WriteLine($"Definition: {version.SchemaDefinition}");
Console.WriteLine($"Registered: {version.RegisteredAt}");
```

### Specific version number

Pass the version number to get an exact version:

```csharp
var version = await client.Registry.GetSchemaVersion("order-placed", versionNumber: 2);
```

### By version ID

If you already have a `SchemaVersionId` (for example, from a `CreateSchema` result), use `GetSchemaVersionById`:

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed", definition, SchemaDataFormat.Json
);

var version = await client.Registry.GetSchemaVersionById(descriptor.VersionId);
```

### Errors

The same exceptions apply to `GetSchemaVersion` and `GetSchemaVersionById`:

| Exception | When it is thrown |
|-----------|------------------|
| `SchemaNotFoundException` | The schema or requested version does not exist. |
| `SchemaDeletedException` | The schema (or the version's parent schema) was deleted. |

## Registering a new version

Once a schema exists, use `RegisterSchemaVersion` to add a new version. The server validates the definition against the schema's configured compatibility mode and rejects incompatible changes.

```csharp
var descriptor = await client.Registry.RegisterSchemaVersion(
    schemaName: "order-placed",
    schemaDefinition: updatedSchemaJson
);

Console.WriteLine($"Registered version {descriptor.VersionNumber}");
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `schemaName` | `SchemaName` | yes | The schema to add a version to. |
| `schemaDefinition` | `string` | yes | The new schema definition. |
| `cancellationToken` | `CancellationToken` | no | Optional cancellation token. |

### Errors

| Exception | When it is thrown |
|-----------|------------------|
| `SchemaNotFoundException` | The schema does not exist. Use `CreateSchema` first. |
| `SchemaDeletedException` | The schema was deleted. |
| `SchemaIncompatibleException` | The new version violates the schema's compatibility mode. The exception's `Errors` property lists each incompatibility. See [Schema compatibility](./schema-compatibility.md). |

## Listing schemas

Use `ListSchemas` to enumerate registered schemas, optionally filtered by name prefix and tags:

```csharp
// List everything
var all = await client.Registry.ListSchemas();

// Filter by name prefix
var orders = await client.Registry.ListSchemas(schemaNamePrefix: "order-");

// Filter by tags (matches schemas with all specified tags)
var fulfillment = await client.Registry.ListSchemas(
    tags: new Dictionary<string, string> { ["team"] = "fulfillment" }
);

// Combined
var ordersFulfillment = await client.Registry.ListSchemas(
    schemaNamePrefix: "order-",
    tags: new Dictionary<string, string> { ["team"] = "fulfillment" }
);

foreach (var schema in all) {
    Console.WriteLine($"{schema.SchemaName} (v{schema.LatestSchemaVersion})");
}
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `schemaNamePrefix` | `string?` | no, defaults to `null` | Returns only schemas whose names start with this prefix. |
| `tags` | `IReadOnlyDictionary<string, string>?` | no, defaults to `null` | Returns only schemas matching all specified tags. |
| `cancellationToken` | `CancellationToken` | no | Optional cancellation token. |

Returns `IReadOnlyList<Schema>`.

## Deleting a schema

`DeleteSchema` removes a schema and all its versions from the registry:

```csharp
await client.Registry.DeleteSchema("order-placed");
```

The method returns `Success` (a marker type indicating the operation completed) on success.

### Errors

| Exception | When it is thrown |
|-----------|------------------|
| `SchemaNotFoundException` | The schema does not exist. |
| `SchemaDeletedException` | The schema was already deleted. |

## Data formats

The `SchemaDataFormat` enum specifies the serialization format for schema definitions:

| Value | Description | Content type |
|-------|-------------|--------------|
| `Json` | JSON Schema | `application/json` |
| `Protobuf` | Protocol Buffers | `application/vnd.google.protobuf` |
| `Avro` | Apache Avro | `application/vnd.apache.avro+json` |
| `Bytes` | Raw bytes (no schema validation) | `application/octet-stream` |

## Compatibility modes

`CompatibilityMode` controls how new schema versions must relate to previous ones. See [Schema compatibility](./schema-compatibility.md) for the full reference and per-mode rules.

## Model reference

### Schema

Returned by `GetSchema` and `ListSchemas`. Contains the schema metadata but not the definition content.

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

Returned by `CreateSchema` and `RegisterSchemaVersion`. A lightweight reference to a schema version.

| Property | Type | Description |
|----------|------|-------------|
| `VersionId` | `SchemaVersionId` | Unique identifier for the created version. |
| `VersionNumber` | `int` | The version number assigned. |

### SchemaName

A strongly-typed wrapper around a schema name string. Supports implicit conversion from `string`:

```csharp
// Explicit construction
var name = SchemaName.From("order-placed");

// Implicit from string, works anywhere SchemaName is expected
SchemaName name = "order-placed";
```

### SchemaVersionId

A strongly-typed wrapper around a `Guid`. Supports implicit conversion from both `Guid` and `string`:

```csharp
var id = SchemaVersionId.From(someGuid);
var id = SchemaVersionId.From("550e8400-e29b-41d4-a716-446655440000");
```

## Error handling

Registry operations follow the same exception-based model as the rest of the client. Catch the specific exception type when you want to handle a known case, and let the rest propagate.

```csharp
try {
    var schema = await client.Registry.GetSchema("order-placed");
    Console.WriteLine($"Found: {schema.SchemaName}");
}
catch (SchemaNotFoundException) {
    // Schema does not exist. Create it.
}
catch (SchemaDeletedException ex) {
    Console.WriteLine($"Schema '{ex.Schema}' was deleted.");
}
```

| Exception | Operations that throw it |
|-----------|--------------------------|
| `SchemaNotFoundException` | `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`, `DeleteSchema`, `CheckSchemaCompatibility` |
| `SchemaAlreadyExistsException` | `CreateSchema` |
| `SchemaDeletedException` | `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`, `DeleteSchema`, `CheckSchemaCompatibility` |
| `SchemaIncompatibleException` | `RegisterSchemaVersion`, `CheckSchemaCompatibility` (when the new definition violates the configured compatibility mode) |

`SchemaNotFoundException`, `SchemaAlreadyExistsException`, and `SchemaDeletedException` each expose a `Schema` property with the affected `SchemaName`. `SchemaIncompatibleException` exposes an `Errors` collection of `SchemaCompatibilityError` records.

For general guidance (cancellation, gRPC errors, validation), see [Error handling](./error-handling.md).

## Complete workflow example

A typical workflow: create a schema, retrieve it, fetch the version details, then clean up.

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

var descriptor = await client.Registry.CreateSchema(
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

Console.WriteLine($"Created schema version {descriptor.VersionNumber} (ID: {descriptor.VersionId})");

// 2. Retrieve the schema metadata
var schema = await client.Registry.GetSchema("order-placed");

Console.WriteLine($"Schema: {schema.SchemaName}");
Console.WriteLine($"Format: {schema.Details.DataFormat}");
Console.WriteLine($"Compatibility: {schema.Details.Compatibility}");
Console.WriteLine($"Description: {schema.Details.Description}");
Console.WriteLine($"Latest version: {schema.LatestSchemaVersion}");

foreach (var tag in schema.Details.Tags) {
    Console.WriteLine($"  Tag: {tag.Key} = {tag.Value}");
}

// 3. Fetch the latest schema version (includes the definition)
var version = await client.Registry.GetSchemaVersion("order-placed");

Console.WriteLine($"Version {version.VersionNumber}:");
Console.WriteLine($"  Registered: {version.RegisteredAt}");
Console.WriteLine($"  Format: {version.DataFormat}");
Console.WriteLine($"  Definition: {version.SchemaDefinition}");

// 4. Clean up
await client.Registry.DeleteSchema("order-placed");
Console.WriteLine("Schema deleted");
```
