---
order: 1
---

# Registry

<CopyMarkdownButton />

Register, retrieve, list, and delete schemas through `client.Registry`. Each schema has a name, a definition, a serialization format, and one or more versions. Compatibility rules are enforced when new versions are registered (see [Compatibility](./compatibility.md)).

Registry operations return values directly and throw on failure. See [Error handling](../error-handling.md) for the exception catalogue and patterns.

## Creating a schema

`CreateSchema` registers a new schema and its first version. It returns a `SchemaVersionDescriptor` containing the new `VersionId` and `VersionNumber`.

```csharp
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

Console.WriteLine("Created version {0} ({1})", descriptor.VersionNumber, descriptor.VersionId);
```

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `schemaName` | `SchemaName` | yes | Unique name for the schema. Accepts a plain `string` via implicit conversion. |
| `schemaDefinition` | `string` | yes | The schema definition as a string (for example, a JSON Schema document). |
| `dataFormat` | `SchemaDataFormat` | yes | The serialization format of the schema content. |
| `compatibilityMode` | `CompatibilityMode` | no | Defaults to `None`. Rules for how future versions must relate to previous versions. |
| `description` | `string` | no | Defaults to `""`. Human-readable description. |
| `tags` | `IReadOnlyDictionary<string, string>?` | no | Defaults to `null`. Key-value metadata for categorization and filtering. |

Throws `SchemaAlreadyExistsException` when the name is taken, or `ArgumentException` when inputs are empty.

## Getting a schema

`GetSchema` returns the schema's metadata and details, but not the definition itself. To get the actual definition, use `GetSchemaVersion`.

```csharp
var schema = await client.Registry.GetSchema("order-placed");

Console.WriteLine("Schema: {0}", schema.SchemaName);
Console.WriteLine("Format: {0}", schema.Details.DataFormat);
Console.WriteLine("Compatibility: {0}", schema.Details.Compatibility);
Console.WriteLine("Latest version: {0}", schema.LatestSchemaVersion);
Console.WriteLine("Created: {0}, Updated: {1}", schema.CreatedAt, schema.UpdatedAt);
```

Throws `SchemaNotFoundException` or `SchemaDeletedException`.

## Getting a schema version

`GetSchemaVersion` returns a specific version including its definition. Omit `versionNumber` for the latest, pass an exact number for a specific version, or call `GetSchemaVersionById` if you already have a `SchemaVersionId` (for example, from a `CreateSchema` result).

```csharp
// Latest version
var latest = await client.Registry.GetSchemaVersion("order-placed");

// Specific version number
var v2 = await client.Registry.GetSchemaVersion("order-placed", versionNumber: 2);

// By version ID
var byId = await client.Registry.GetSchemaVersionById(descriptor.VersionId);

Console.WriteLine("Version {0} registered at {1}", latest.VersionNumber, latest.RegisteredAt);
Console.WriteLine("Definition: {0}", latest.SchemaDefinition);
```

Both methods throw `SchemaNotFoundException` if the schema or requested version does not exist, or `SchemaDeletedException` if the parent schema was deleted.

## Registering a new version

`RegisterSchemaVersion` adds a new version to an existing schema. The server validates the definition against the schema's configured compatibility mode and rejects incompatible changes.

```csharp
var descriptor = await client.Registry.RegisterSchemaVersion(
    schemaName: "order-placed",
    schemaDefinition: updatedSchemaJson
);

Console.WriteLine("Registered version {0}", descriptor.VersionNumber);
```

Throws `SchemaNotFoundException` (use `CreateSchema` first), `SchemaDeletedException`, or `SchemaIncompatibleException`. The `Errors` property on `SchemaIncompatibleException` lists each incompatibility. See [Compatibility](./compatibility.md) for the rules.

## Listing schemas

`ListSchemas` enumerates registered schemas, optionally filtered by name prefix and tags. Returns `IReadOnlyList<Schema>`.

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
    Console.WriteLine("{0} (v{1})", schema.SchemaName, schema.LatestSchemaVersion);
}
```

## Deleting a schema

`DeleteSchema` removes a schema and all its versions from the registry. Returns `Success` (a marker type) on completion.

```csharp
await client.Registry.DeleteSchema("order-placed");
```

Throws `SchemaNotFoundException` or `SchemaDeletedException`.

## Data formats

`SchemaDataFormat` specifies the serialization format for schema definitions:

| Value | Description | Content type |
|-------|-------------|--------------|
| `Json` | JSON Schema | `application/json` |
| `Protobuf` | Protocol Buffers | `application/vnd.google.protobuf` |
| `Avro` | Apache Avro | `application/vnd.apache.avro+json` |
| `Bytes` | Raw bytes (no schema validation) | `application/octet-stream` |

## Compatibility modes

`CompatibilityMode` controls how new schema versions must relate to previous ones. See [Compatibility](./compatibility.md) for the full reference and per-mode rules.

## Models

`Schema` is returned by `GetSchema` and `ListSchemas`. It carries metadata but not the definition.

| Property | Type | Description |
|----------|------|-------------|
| `SchemaName` | `SchemaName` | Unique name of the schema. |
| `Details` | `SchemaDetails` | Configuration: `DataFormat`, `Compatibility`, `Description`, `Tags`. |
| `LatestSchemaVersion` | `int` | Most recent version number. |
| `CreatedAt` | `DateTimeOffset` | When the schema was first created. |
| `UpdatedAt` | `DateTimeOffset` | When the schema was last modified. |

`SchemaVersion` is returned by `GetSchemaVersion` and `GetSchemaVersionById`. It includes the definition content.

| Property | Type | Description |
|----------|------|-------------|
| `VersionId` | `SchemaVersionId` | Unique identifier for this version. |
| `VersionNumber` | `int` | Sequential version number within the schema. |
| `SchemaDefinition` | `string` | The schema definition content. |
| `DataFormat` | `SchemaDataFormat` | Serialization format of the definition. |
| `RegisteredAt` | `DateTimeOffset` | When this version was registered. |

`SchemaVersionDescriptor` is returned by `CreateSchema` and `RegisterSchemaVersion`. It is a lightweight reference holding just `VersionId` and `VersionNumber`.

`SchemaName` and `SchemaVersionId` are strongly-typed wrappers. Both support implicit conversion from `string`; `SchemaVersionId` also accepts a `Guid`.

```csharp
SchemaName name = "order-placed";
var id = SchemaVersionId.From(someGuid);
```

## Error handling

Registry operations follow the same exception model as the rest of the client. Catch the specific exception type when you want to handle a known case, and let the rest propagate.

```csharp
try {
    var schema = await client.Registry.GetSchema("order-placed");
    Console.WriteLine("Found: {0}", schema.SchemaName);
}
catch (SchemaNotFoundException) {
    // Schema does not exist. Create it.
}
catch (SchemaDeletedException ex) {
    Console.WriteLine("Schema '{0}' was deleted.", ex.Schema);
}
```

| Exception | Operations that throw it |
|-----------|--------------------------|
| `SchemaNotFoundException` | `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`, `DeleteSchema`, `CheckSchemaCompatibility` |
| `SchemaAlreadyExistsException` | `CreateSchema` |
| `SchemaDeletedException` | `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`, `DeleteSchema`, `CheckSchemaCompatibility` |
| `SchemaIncompatibleException` | `RegisterSchemaVersion`, `CheckSchemaCompatibility` (when the new definition violates the configured compatibility mode) |

`SchemaNotFoundException`, `SchemaAlreadyExistsException`, and `SchemaDeletedException` each expose a `Schema` property with the affected `SchemaName`. `SchemaIncompatibleException` exposes an `Errors` collection of `SchemaCompatibilityError` records.

For general guidance (cancellation, gRPC errors, validation), see [Error handling](../error-handling.md).

## Example workflow

A typical workflow: create a schema, retrieve it, fetch the version details, then clean up.

```csharp
var client = new KurrentClient(options);

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

var schema = await client.Registry.GetSchema("order-placed");
var version = await client.Registry.GetSchemaVersion("order-placed");

Console.WriteLine("Schema: {0}, latest version: {1}", schema.SchemaName, version.VersionNumber);
Console.WriteLine("Format: {0}, compatibility: {1}", schema.Details.DataFormat, schema.Details.Compatibility);
Console.WriteLine("Definition: {0}", version.SchemaDefinition);

await client.Registry.DeleteSchema("order-placed");
```
