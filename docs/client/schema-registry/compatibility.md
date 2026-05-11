---
order: 2
---

# Compatibility

<CopyMarkdownButton />

Schema compatibility governs how schemas evolve over time. When producers and consumers share a schema, breaking changes can leave one side unable to read the other. The registry enforces a compatibility mode on every schema and rejects new versions that violate it.

Pick a mode when you create a schema. The registry validates each subsequent version against its rules.

## Compatibility modes

`CompatibilityMode` controls how new versions must relate to existing ones.

| Mode          | Value | Description                                                                  |
|---------------|-------|------------------------------------------------------------------------------|
| `Backward`    | 1     | New schemas can read data written by the previous version.                   |
| `Forward`     | 2     | The previous schema can read data written by the new version.                |
| `Full`        | 3     | Both backward and forward compatible with the previous version.              |
| `BackwardAll` | 4     | New schemas can read data written by every previous version.                 |
| `ForwardAll`  | 5     | Every previous version can read data written by new schemas.                 |
| `FullAll`     | 6     | Combines `BackwardAll` and `ForwardAll`. The strongest guarantee.            |
| `None`        | 7     | No compatibility checks. Any change is accepted.                             |

`Unspecified` (0) is the proto default and should not be used.

::: tabs#compat-mode

@tab Backward

A new version is backward compatible if it can read data produced by the previous version. Use this when consumers must process historical events.

- **Allowed:** add optional fields, remove fields that had defaults.
- **Disallowed:** remove required fields, change a field's type incompatibly.

```csharp
await client.Registry.CreateSchema(
    "user-registered", schemaJson, SchemaDataFormat.Json, CompatibilityMode.Backward);
```

`BackwardAll` extends the guarantee to every prior version, not just the immediately preceding one.

@tab Forward

A new version is forward compatible if data written with it can be read by the previous version. Use this when producers upgrade before consumers.

- **Allowed:** remove optional fields, add fields (old readers ignore them).
- **Disallowed:** add new required fields, change a field's type.

```csharp
await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json, CompatibilityMode.Forward);
```

`ForwardAll` extends the guarantee to every prior version.

@tab Full

Both old and new schemas can read data written by the other. The strongest practical contract for evolving shared schemas.

- **Allowed:** add or remove optional fields with defaults.

```csharp
await client.Registry.CreateSchema(
    "payment-processed", schemaJson, SchemaDataFormat.Json, CompatibilityMode.Full);
```

`FullAll` requires the same guarantees against every prior version.

@tab None

Disables compatibility enforcement. Any change is accepted, including breaking ones. Appropriate for development environments or schemas that are not shared across services.

```csharp
await client.Registry.CreateSchema(
    "internal-debug-event", schemaJson, SchemaDataFormat.Json, CompatibilityMode.None);
```

:::

## Choosing a mode

| Scenario                                | Recommended                | Rationale                                          |
|-----------------------------------------|----------------------------|----------------------------------------------------|
| Consumers upgrade before producers      | `Backward`                 | New consumers must read old data.                  |
| Producers upgrade before consumers      | `Forward`                  | Old consumers must read new data.                  |
| Independent upgrade schedules           | `Full`                     | Either side may upgrade first.                     |
| Long-lived event log, replays from v1   | `BackwardAll` or `FullAll` | Readers must handle every historical version.      |
| Shared events across many services      | `Full` or `FullAll`        | Minimizes coordination overhead.                   |
| Internal single-service events          | `Backward` or `None`       | Few consumers to coordinate.                       |
| Rapid prototyping or development        | `None`                     | Breaking changes are expected and acceptable.      |

## Check before registering

`CheckSchemaCompatibility` validates a proposed definition against an existing schema without registering it. It returns the matched `SchemaVersionId` on success and throws `SchemaIncompatibleException` on failure.

The first parameter is a `SchemaIdentifier`. Pass a `SchemaName` to check against the latest version, or a `SchemaVersionId` to check against a specific one.

::: tabs#check-target

@tab Latest version

```csharp
var versionId = await client.Registry.CheckSchemaCompatibility(
    SchemaName.From("order-placed"), proposedSchema, SchemaDataFormat.Json);
```

@tab Specific version

```csharp
var versionId = await client.Registry.CheckSchemaCompatibility(
    SchemaVersionId.From("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
    proposedSchema,
    SchemaDataFormat.Json);
```

Useful when verifying that a rollback would be safe.

:::

| Parameter           | Type                | Description                                                  |
|---------------------|---------------------|--------------------------------------------------------------|
| `identifier`        | `SchemaIdentifier`  | Schema name or version id to check against.                  |
| `schemaDefinition`  | `string`            | The proposed new definition.                                 |
| `dataFormat`        | `SchemaDataFormat`  | `Json`, `Protobuf`, `Avro`, or `Bytes`.                      |
| `cancellationToken` | `CancellationToken` | Optional.                                                    |

## Compatibility errors

`SchemaIncompatibleException.Errors` is a `SchemaCompatibilityErrors` collection. Each entry is a `SchemaCompatibilityError` describing one incompatibility. `ToString()` produces a one-line summary like `IncompatibleTypeChange at 'properties.quantity': Type changed from Integer to String`.

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

| Property       | Type                            | Description                             |
|----------------|---------------------------------|-----------------------------------------|
| `Kind`         | `SchemaCompatibilityErrorKind`  | Category of incompatibility.            |
| `Details`      | `string`                        | Human-readable description.             |
| `PropertyPath` | `string`                        | JSON path to the incompatible property. |
| `OriginalType` | `JsonObjectType`                | Type in the existing schema.            |
| `NewType`      | `JsonObjectType`                | Type in the proposed schema.            |

`SchemaCompatibilityErrorKind` enumerates the categories.

| Kind                       | Direction | Description                                                                |
|----------------------------|-----------|----------------------------------------------------------------------------|
| `MissingRequiredProperty`  | Backward  | A required property from the old schema is missing in the new schema.      |
| `IncompatibleTypeChange`   | Backward  | A property's type changed incompatibly between versions.                   |
| `OptionalToRequired`       | Backward  | A property changed from optional to required.                              |
| `NewRequiredProperty`      | Forward   | A new required property was added that old readers cannot provide.         |
| `RemovedProperty`          | Forward   | A property was removed that old readers still expect.                      |
| `ArrayTypeIncompatibility` | Both      | Array item types are incompatible between versions.                        |
| `DataFormatMismatch`       | Both      | The proposed data format does not match the existing schema.               |

`Unspecified` is the proto default and should not occur in practice.

`CheckSchemaCompatibility` and `RegisterSchemaVersion` share the same exception model: `SchemaIncompatibleException`, `SchemaNotFoundException`, `SchemaDeletedException`, and `ArgumentException` for invalid input.

## Evolving a schema safely

A typical workflow: create a schema, propose a new version, check compatibility, register it.

```csharp
await client.Registry.CreateSchema(
    "order-placed", v1Json, SchemaDataFormat.Json, CompatibilityMode.Backward);

try {
    await client.Registry.CheckSchemaCompatibility(
        SchemaName.From("order-placed"), v2Json, SchemaDataFormat.Json);

    var v2 = await client.Registry.RegisterSchemaVersion("order-placed", v2Json);
    Console.WriteLine("Registered version {0}.", v2.VersionNumber);
}
catch (SchemaIncompatibleException ex) {
    foreach (var error in ex.Errors) {
        Console.WriteLine(error);
    }
}
```

Adding an optional field without changing `required` is backward compatible. Adding the field to `required` instead would fail with a `NewRequiredProperty` error: existing data written without it could not satisfy the new schema.
