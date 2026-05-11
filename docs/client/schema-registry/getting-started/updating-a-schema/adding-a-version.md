---
order: 2
---

# Adding a schema version

<CopyMarkdownButton />

`RegisterSchemaVersion` adds a new version to an existing schema. The server validates the new definition against the schema's compatibility mode and rejects incompatible changes. The return value is a `SchemaVersionDescriptor` carrying the new `VersionId` and `VersionNumber`.

## Register a new version

```csharp
var descriptor = await client.Registry.RegisterSchemaVersion("order-placed", updatedSchemaJson);

Console.WriteLine("Registered version {0}", descriptor.VersionNumber);
```

The data format is inherited from the schema. The compatibility check uses the configured [compatibility mode](../../compatibility-modes.md).

## Check before you register

For higher-stakes changes, run [Checking compatibility](./checking-compatibility.md) first. The check uses the same rules as registration but does not modify the registry, so a passing check followed by a failing register is unusual outside of races.

```csharp
try {
    await client.Registry.CheckSchemaCompatibility(
        SchemaName.From("order-placed"), v2Json, SchemaDataFormat.Json);

    var v2 = await client.Registry.RegisterSchemaVersion("order-placed", v2Json);
    Console.WriteLine("Registered version {0}", v2.VersionNumber);
}
catch (SchemaIncompatibleException ex) {
    foreach (var error in ex.Errors) {
        Console.WriteLine(error);
    }
}
```

Adding an optional field without changing `required` is backward compatible. Adding the field to `required` instead fails with a `NewRequiredProperty` error: existing data written without the field could not satisfy the new schema.

## Errors

| Exception                       | When it fires                                                                  |
|---------------------------------|--------------------------------------------------------------------------------|
| `SchemaIncompatibleException`   | The new definition violates the configured compatibility mode.                 |
| `SchemaNotFoundException`       | The schema does not exist. Use [Creating a schema](../creating-a-schema.md) first. |
| `SchemaDeletedException`        | The schema was deleted.                                                        |
