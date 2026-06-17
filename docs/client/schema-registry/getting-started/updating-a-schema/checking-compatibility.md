---
order: 1
---

# Checking compatibility

<CopyMarkdownButton />

`CheckSchemaCompatibility` validates a proposed definition against an existing schema without registering it. Useful for pre-flight checks in CI, for evaluating a candidate version before committing, or for rollback dry-runs against a specific historical version.

The call returns the matched `SchemaVersionId` on success and throws `SchemaIncompatibleException` on failure. The check uses the same rules the server applies on `RegisterSchemaVersion`.

## Run a compatibility check

Create a `CheckSchemaCompatibilityOptions`, point it at a schema, and submit.

```csharp
var versionId = await client.Registry.CheckSchemaCompatibility(new CheckSchemaCompatibilityOptions {
    Identifier       = SchemaName.From("order-placed"),
    SchemaDefinition = proposedSchema,
    DataFormat       = SchemaDataFormat.Json
});
```

| Property           | Type               | Description                                                 |
|--------------------|--------------------|-------------------------------------------------------------|
| `Identifier`       | `SchemaIdentifier` | Target schema. Accepts a `SchemaName` or `SchemaVersionId`. |
| `SchemaDefinition` | `string`           | The proposed new definition.                                |
| `DataFormat`       | `SchemaDataFormat` | `Json`, `Protobuf`, `Avro`, or `Bytes`.                     |

A `SchemaName` checks against the latest registered version. A `SchemaVersionId` checks against a specific one, handy when validating a rollback or a parallel version.

::: tip
An overload skips the request type for the common case:

```csharp
var versionId = await client.Registry.CheckSchemaCompatibility(
    SchemaName.From("order-placed"), proposedSchema, SchemaDataFormat.Json);
```

The same overload accepts a `SchemaVersionId` when targeting a specific version.
:::

## Inspect failures

`SchemaIncompatibleException.Errors` lists every incompatibility. Each entry's `ToString()` produces a one-line summary like `IncompatibleTypeChange at 'properties.quantity': Type changed from Integer to String`.

```csharp
try {
    await client.Registry.CheckSchemaCompatibility(
        SchemaName.From("order-placed"), proposedSchema, SchemaDataFormat.Json);
}
catch (SchemaIncompatibleException ex) {
    foreach (var error in ex.Errors) {
        Console.WriteLine("{0} at '{1}': {2}", error.Kind, error.PropertyPath, error.Details);
    }
}
```

The error categories and their meanings live on the [Compatibility modes](../../compatibility-modes.md#compatibility-errors) page.

## Errors

| Exception                       | When it fires                                              |
|---------------------------------|------------------------------------------------------------|
| `SchemaIncompatibleException`   | The proposed definition violates the configured mode.      |
| `SchemaNotFoundException`       | The target schema or version does not exist.               |
| `SchemaDeletedException`        | The target schema was deleted.                             |
| `ValidationException`           | The request itself is missing required fields.             |
