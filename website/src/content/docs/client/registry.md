---
title: Registry
description: Create schemas, add versions, check compatibility, and inspect what is registered.
---

`client.Registry` manages schema definitions and their versions. A schema has a name, a data format,
a compatibility mode, and an ordered list of versions.

Most applications never call this module directly, because
[registration](/kurrent-surge/schema/registration/) does it for you when you turn it on. Reach for it when you
manage schemas out of band, or need to inspect what is registered.


## Create a schema

Registers the schema and its first version in one call. Pick the compatibility mode here. The
registry enforces it on every later version.

```csharp
var descriptor = await client.Registry.CreateSchema(new CreateSchemaOptions {
    SchemaName        = "order-placed",
    SchemaDefinition  = schemaJson,
    DataFormat        = SchemaDataFormat.Json,
    CompatibilityMode = CompatibilityMode.Backward,
    Description       = "Order placed event",
    Tags              = new Dictionary<string, string> { ["domain"] = "orders" }
});
```

Or the short form:

```csharp
var descriptor = await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json, CompatibilityMode.Backward);
```

**Returns `SchemaVersionDescriptor`:** `VersionId` and `VersionNumber`.

`VersionId` is the durable handle. Hold it in code that binds to a specific contract.
`VersionNumber` is sequential per schema and convenient for humans, but it is not an identifier.

| Option | Type | Meaning |
|---|---|---|
| `SchemaName` | `SchemaName` | Unique per registry |
| `SchemaDefinition` | `string` | The definition itself |
| `DataFormat` | `SchemaDataFormat` | `Json`, `Protobuf`, `Avro`, or `Bytes`. Defaults to `Unspecified`, which the request rejects — set it |
| `CompatibilityMode` | `CompatibilityMode` | Defaults to `None` |
| `Description` | `string` | Free text |
| `Tags` | `IDictionary<string,string>` | For search and discovery |

Throws `SchemaAlreadyExistsException` if the name is taken.

## Add a version

New versions inherit the schema's data format. The server checks the definition against the
schema's [compatibility mode](/kurrent-surge/schema/evolution/) and rejects anything outside it.

```csharp
var v2 = await client.Registry.RegisterSchemaVersion("order-placed", updatedJson);
```

Throws `SchemaIncompatibleException`, whose `Errors` list every violation with a property path, or
`SchemaNotFoundException` if the schema does not exist.

## Check without registering

Same rules as registration, no change to the registry. Use it in CI, or before committing to a
version.

```csharp
SchemaVersionId matched = await client.Registry.CheckSchemaCompatibility(
    SchemaName.From("order-placed"),
    opts => {
        opts.SchemaDefinition = proposedJson;
        opts.DataFormat       = SchemaDataFormat.Json;
    });
```

`Identifier` accepts either a `SchemaName`, which checks against the latest version, or a
`SchemaVersionId`, which checks against that specific one. The latter is what you want for a rollback
dry-run.

## Read schemas

`GetSchema` returns metadata only. `GetSchemaVersion` returns a version including its definition.

```csharp
Schema schema = await client.Registry.GetSchema("order-placed");

SchemaVersion latest = await client.Registry.GetSchemaVersion("order-placed");
SchemaVersion v2     = await client.Registry.GetSchemaVersion("order-placed", versionNumber: 2);
SchemaVersion byId   = await client.Registry.GetSchemaVersionById(descriptor.VersionId);
```

All throw `SchemaNotFoundException` for an unknown name or number, and `SchemaDeletedException` if
the parent schema was deleted.

## List schemas

```csharp
IReadOnlyList<Schema> all = await client.Registry.ListSchemas();

IReadOnlyList<Schema> orders = await client.Registry.ListSchemas(schemaNamePrefix: "order-");

IReadOnlyList<Schema> filtered = await client.Registry.ListSchemas(new ListSchemasOptions {
    SchemaNamePrefix = "order-",
    Tags             = new Dictionary<string, string> { ["team"] = "fulfillment" }
});
```

Tag filters require every named tag to match. No filters returns everything.

## Delete a schema

Removes the schema and every version it holds.

```csharp
Success ok = await client.Registry.DeleteSchema("order-placed");
```

Destructive. Consumers holding a `SchemaVersionId` from this schema start receiving
`SchemaDeletedException`. Coordinate before deleting anything shared.

## Exceptions

| Exception | Cause |
|---|---|
| `SchemaAlreadyExistsException` | The name is already registered |
| `SchemaNotFoundException` | No such schema, version number, or version id |
| `SchemaDeletedException` | The schema was deleted |
| `SchemaIncompatibleException` | The definition violates the compatibility mode. See `Errors` |
| `ValidationException` | The request is missing required fields |
