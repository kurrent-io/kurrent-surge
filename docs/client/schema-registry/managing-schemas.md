---
order: 1
---

# Managing schemas

<CopyMarkdownButton />

CRUD operations for schemas and their versions, all on `client.Registry`. Calls return values directly and throw on failure. See [Error handling](../error-handling.md) for the general exception model.

## Create a schema

`CreateSchema` registers a new schema and its first version. The minimum needed is the name, the definition, and the data format. It returns a `SchemaVersionDescriptor` with the new `VersionId` and `VersionNumber`.

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json);

Console.WriteLine("Created version {0}", descriptor.VersionNumber);
```

Add a compatibility mode, description, and tags for richer metadata.

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed",
    schemaJson,
    SchemaDataFormat.Json,
    CompatibilityMode.Backward,
    description: "Order placed event",
    tags: new Dictionary<string, string> { ["domain"] = "orders" });
```

| Parameter           | Type                                   | Required | Default     | Description                                               |
|---------------------|----------------------------------------|----------|-------------|-----------------------------------------------------------|
| `schemaName`        | `SchemaName`                           | yes      |             | Implicit from `string`.                                   |
| `schemaDefinition`  | `string`                               | yes      |             | The schema definition (for example, JSON Schema).         |
| `dataFormat`        | `SchemaDataFormat`                     | yes      |             | `Json`, `Protobuf`, `Avro`, or `Bytes`.                   |
| `compatibilityMode` | `CompatibilityMode`                    | no       | `None`      | See [Compatibility](./compatibility.md).                  |
| `description`       | `string`                               | no       | `""`        | Human-readable description.                               |
| `tags`              | `IReadOnlyDictionary<string, string>?` | no       | `null`      | Key-value metadata for categorization and filtering.      |

Throws `SchemaAlreadyExistsException` when the name is taken, or `ArgumentException` when inputs are empty.

## Get a schema

`GetSchema` returns metadata and configuration but not the definition. Use `GetSchemaVersion` for the definition.

```csharp
var schema = await client.Registry.GetSchema("order-placed");

Console.WriteLine("{0} (v{1}, {2}, {3})",
    schema.SchemaName,
    schema.LatestSchemaVersion,
    schema.Details.DataFormat,
    schema.Details.Compatibility);
```

Throws `SchemaNotFoundException` or `SchemaDeletedException`.

## Get a schema version

`GetSchemaVersion` returns a version including its definition. `GetSchemaVersionById` is the sibling for when you have a `SchemaVersionId` (for example, from a `CreateSchema` result).

::: tabs#get-version

@tab Latest

```csharp
var latest = await client.Registry.GetSchemaVersion("order-placed");
```

@tab Specific number

```csharp
var v2 = await client.Registry.GetSchemaVersion("order-placed", versionNumber: 2);
```

@tab By id

```csharp
var version = await client.Registry.GetSchemaVersionById(descriptor.VersionId);
```

:::

Both methods throw `SchemaNotFoundException` if the schema or version does not exist, or `SchemaDeletedException` if the parent schema was deleted.

## Register a new version

`RegisterSchemaVersion` adds a new version to an existing schema. The server validates the new definition against the schema's compatibility mode and rejects incompatible changes. See [Compatibility](./compatibility.md) for the rules and the `CheckSchemaCompatibility` operation.

```csharp
var descriptor = await client.Registry.RegisterSchemaVersion("order-placed", updatedSchemaJson);
```

Throws `SchemaNotFoundException` (use `CreateSchema` first), `SchemaDeletedException`, or `SchemaIncompatibleException`. The `Errors` property on `SchemaIncompatibleException` lists each incompatibility.

## List schemas

`ListSchemas` enumerates registered schemas, optionally filtered by name prefix and tags. Tag filters require all specified tags to match. Returns `IReadOnlyList<Schema>`.

::: tabs#list-schemas

@tab All

```csharp
var all = await client.Registry.ListSchemas();
```

@tab By prefix

```csharp
var orders = await client.Registry.ListSchemas(schemaNamePrefix: "order-");
```

@tab By tags

```csharp
var fulfillment = await client.Registry.ListSchemas(
    tags: new Dictionary<string, string> { ["team"] = "fulfillment" });
```

@tab Combined

```csharp
var ordersFulfillment = await client.Registry.ListSchemas(
    schemaNamePrefix: "order-",
    tags: new Dictionary<string, string> { ["team"] = "fulfillment" });
```

:::

## Delete a schema

`DeleteSchema` removes a schema and all its versions.

```csharp
await client.Registry.DeleteSchema("order-placed");
```

Throws `SchemaNotFoundException` or `SchemaDeletedException`.

## Data formats

`SchemaDataFormat` specifies the serialization format for schema definitions.

| Value      | Description                       | Content type                          |
|------------|-----------------------------------|---------------------------------------|
| `Json`     | JSON Schema                       | `application/json`                    |
| `Protobuf` | Protocol Buffers                  | `application/vnd.google.protobuf`     |
| `Avro`     | Apache Avro                       | `application/vnd.apache.avro+json`    |
| `Bytes`    | Raw bytes (no schema validation)  | `application/octet-stream`            |

## Models

`Schema` is returned by `GetSchema` and `ListSchemas`. It carries metadata but not the definition.

| Property              | Type             | Description                                                       |
|-----------------------|------------------|-------------------------------------------------------------------|
| `SchemaName`          | `SchemaName`     | Unique name of the schema.                                        |
| `Details`             | `SchemaDetails`  | `DataFormat`, `Compatibility`, `Description`, `Tags`.             |
| `LatestSchemaVersion` | `int`            | Most recent version number.                                       |
| `CreatedAt`           | `DateTimeOffset` | When the schema was first created.                                |
| `UpdatedAt`           | `DateTimeOffset` | When the schema was last modified.                                |

`SchemaVersion` is returned by `GetSchemaVersion` and `GetSchemaVersionById`. It includes the definition content.

| Property            | Type                | Description                                       |
|---------------------|---------------------|---------------------------------------------------|
| `VersionId`         | `SchemaVersionId`   | Unique identifier for this version.               |
| `VersionNumber`     | `int`               | Sequential version number within the schema.      |
| `SchemaDefinition`  | `string`            | The schema definition content.                    |
| `DataFormat`        | `SchemaDataFormat`  | Serialization format of the definition.           |
| `RegisteredAt`      | `DateTimeOffset`    | When this version was registered.                 |

`SchemaVersionDescriptor` is returned by `CreateSchema` and `RegisterSchemaVersion`. It is a lightweight reference holding only `VersionId` and `VersionNumber`.

`SchemaName` and `SchemaVersionId` are strongly-typed wrappers. Both convert implicitly from `string`. `SchemaVersionId` also accepts a `Guid`.

```csharp
SchemaName name = "order-placed";
SchemaVersionId id = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
```

## Errors

| Exception                       | Operations that throw it                                                                            |
|---------------------------------|-----------------------------------------------------------------------------------------------------|
| `SchemaNotFoundException`       | `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`, `DeleteSchema`    |
| `SchemaAlreadyExistsException`  | `CreateSchema`                                                                                      |
| `SchemaDeletedException`        | `GetSchema`, `GetSchemaVersion`, `GetSchemaVersionById`, `RegisterSchemaVersion`, `DeleteSchema`    |
| `SchemaIncompatibleException`   | `RegisterSchemaVersion` when the new definition violates the configured compatibility mode.        |

`SchemaNotFoundException`, `SchemaAlreadyExistsException`, and `SchemaDeletedException` each expose a `Schema` property with the affected `SchemaName`. `SchemaIncompatibleException` exposes an `Errors` collection. See [Compatibility](./compatibility.md#compatibility-errors).

```csharp
try {
    var schema = await client.Registry.GetSchema("order-placed");
}
catch (SchemaNotFoundException) {
    // Schema does not exist. Create it.
}
catch (SchemaDeletedException ex) {
    Console.WriteLine("Schema '{0}' was deleted.", ex.Schema);
}
```

For general guidance (cancellation, gRPC errors, validation), see [Error handling](../error-handling.md).
