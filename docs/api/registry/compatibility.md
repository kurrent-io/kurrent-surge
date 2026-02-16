---
order: 2
---

# Schema compatibility

Schema compatibility governs how schemas are allowed to evolve over time. When consumers and producers share a schema, changes to that schema can break existing readers or writers unless the changes follow well-defined rules. The schema registry enforces these rules automatically through compatibility modes.

By choosing the right compatibility mode when you create a schema, you ensure that new versions of a schema can coexist with older versions without disrupting event producers or consumers that have not yet upgraded.

## Compatibility modes

The `CompatibilityMode` enum defines how the registry validates new schema versions against existing ones.

### CompatibilityMode reference

| Mode       | Value | Description                                                               |
|------------|-------|---------------------------------------------------------------------------|
| `None`     | 4     | No compatibility checks. Any schema change is accepted. Use with caution. |
| `Backward` | 1     | New schemas can read data written by the previous schema version.         |
| `Forward`  | 2     | Previous schemas can read data written by the new schema version.         |
| `Full`     | 3     | Both backward and forward compatible with the previous schema version.    |

### Backward compatibility

A new schema version is **backward compatible** if it can read data produced by the previous version. This is the most common mode for consumers that need to process historical events.

Allowed changes:
- Adding optional fields
- Removing fields that had defaults

Disallowed changes:
- Removing required fields
- Changing a field's type incompatibly

```csharp
// Data written with v1 (has "name") can be read by v2 (has "name" + optional "age")
var result = await client.Registry.CreateSchema(
    schemaName: "user-registered",
    schemaDefinition: schemaV2Json,
    dataFormat: SchemaDataFormat.Json,
    compatibilityMode: CompatibilityMode.Backward,
    description: "User registration event",
    tags: new Dictionary<string, string> { ["domain"] = "identity" }
);
```

### Forward compatibility

A new schema version is **forward compatible** if data written with it can be read by the previous version. This is useful when producers upgrade before consumers.

Allowed changes:
- Removing optional fields
- Adding fields (old readers ignore them)

Disallowed changes:
- Adding new required fields (old readers cannot supply them)
- Changing a field's type

```csharp
var result = await client.Registry.CreateSchema(
    schemaName: "order-placed",
    schemaDefinition: schemaJson,
    dataFormat: SchemaDataFormat.Json,
    compatibilityMode: CompatibilityMode.Forward,
    description: "Order placed event",
    tags: new Dictionary<string, string> { ["domain"] = "orders" }
);
```

### Full compatibility

Combines backward and forward compatibility. Both old and new schemas can read data written by the other. This is the strictest practical mode.

Allowed changes:
- Adding optional fields with defaults
- Removing optional fields with defaults

```csharp
var result = await client.Registry.CreateSchema(
    schemaName: "payment-processed",
    schemaDefinition: schemaJson,
    dataFormat: SchemaDataFormat.Json,
    compatibilityMode: CompatibilityMode.Full,
    description: "Payment processed event",
    tags: new Dictionary<string, string> { ["domain"] = "billing" }
);
```

### None

Disables all compatibility enforcement. Any schema change is accepted, including breaking changes. Appropriate for development environments or schemas that are not shared across services.

```csharp
var result = await client.Registry.CreateSchema(
    schemaName: "internal-debug-event",
    schemaDefinition: schemaJson,
    dataFormat: SchemaDataFormat.Json,
    compatibilityMode: CompatibilityMode.None,
    description: "Internal debugging event, not shared",
    tags: []
);
```

## Checking compatibility

Before registering a new schema version, you can verify whether a proposed schema definition is compatible with an existing schema. The `CheckSchemaCompatibility` method validates the new definition against the compatibility rules of the target schema.

### Method signature

```csharp
public async ValueTask<Result<SchemaVersionId, CheckSchemaCompatibilityError>> CheckSchemaCompatibility(
    SchemaIdentifier identifier,
    string schemaDefinition,
    SchemaDataFormat dataFormat,
    CancellationToken cancellationToken = default
)
```

### Parameters

| Parameter           | Type                | Description                                                                                                                                        |
|---------------------|---------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|
| `identifier`        | `SchemaIdentifier`  | The schema to check against. Can be a `SchemaName` (checks against the latest version) or a `SchemaVersionId` (checks against a specific version). |
| `schemaDefinition`  | `string`            | The proposed new schema definition.                                                                                                                |
| `dataFormat`        | `SchemaDataFormat`  | The data format of the schema (`Json`, `Protobuf`, `Avro`, `Bytes`).                                                                               |
| `cancellationToken` | `CancellationToken` | Optional cancellation token.                                                                                                                       |

### Identifying the target schema

`SchemaIdentifier` is a discriminated union that accepts either a `SchemaName` or a `SchemaVersionId`. Implicit conversions make both natural to use:

```csharp
// Check against the latest version of a schema by name
SchemaIdentifier byName = SchemaName.From("order-placed");

// Check against a specific schema version by ID
SchemaIdentifier byVersion = SchemaVersionId.From("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
```

### Check by schema name

When you pass a `SchemaName`, the registry checks compatibility against the latest version of that schema:

```csharp
var proposedSchema = """
    {
        "type": "object",
        "properties": {
            "orderId": { "type": "string" },
            "product": { "type": "string" },
            "quantity": { "type": "integer" },
            "discount": { "type": "number" }
        },
        "required": ["orderId", "product", "quantity"]
    }
    """;

var result = await client.Registry.CheckSchemaCompatibility(
    SchemaName.From("order-placed"),
    proposedSchema,
    SchemaDataFormat.Json
);

result.Switch(
    versionId => Console.WriteLine($"Compatible. Version ID: {versionId}"),
    error => Console.WriteLine($"Incompatible: {error.ErrorMessage}")
);
```

### Check by schema version ID

When you pass a `SchemaVersionId`, the registry checks compatibility against that specific version rather than the latest:

```csharp
var result = await client.Registry.CheckSchemaCompatibility(
    SchemaVersionId.From("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
    proposedSchema,
    SchemaDataFormat.Json
);
```

This is useful when you need to verify compatibility against a version that is not the latest, for example when testing whether a rollback would be safe.

## Compatibility error details

When a compatibility check fails, the error provides structured information about what went wrong.

### SchemaCompatibilityError

Each individual incompatibility is represented by a `SchemaCompatibilityError`:

| Property       | Type                           | Description                                 |
|----------------|--------------------------------|---------------------------------------------|
| `Kind`         | `SchemaCompatibilityErrorKind` | The category of incompatibility.            |
| `Details`      | `string`                       | A human-readable description of the issue.  |
| `PropertyPath` | `string`                       | The JSON path to the incompatible property. |
| `OriginalType` | `JsonObjectType`               | The type in the original schema.            |
| `NewType`      | `JsonObjectType`               | The type in the proposed schema.            |

The `ToString()` method produces a summary like:

```
IncompatibleTypeChange at 'properties.quantity': Type changed from Integer to String
```

### SchemaCompatibilityErrorKind

| Kind                       | Direction | Description                                                                |
|----------------------------|-----------|----------------------------------------------------------------------------|
| `MissingRequiredProperty`  | Backward  | A required property from the old schema is missing in the new schema.      |
| `IncompatibleTypeChange`   | Backward  | A property's type changed incompatibly between versions.                   |
| `OptionalToRequired`       | Backward  | A property changed from optional to required.                              |
| `NewRequiredProperty`      | Forward   | A new required property was added that old readers cannot provide.         |
| `RemovedProperty`          | Forward   | A property was removed that old readers still expect.                      |
| `ArrayTypeIncompatibility` | Both      | Array item types are incompatible between versions.                        |
| `DataFormatMismatch`       | Both      | The data format of the proposed schema does not match the existing schema. |

### SchemaCompatibilityErrors collection

Multiple incompatibilities are grouped into a `SchemaCompatibilityErrors` collection, which implements `IEnumerable<SchemaCompatibilityError>`:

```csharp
// Assuming you have access to the errors collection
foreach (var error in compatibilityErrors) {
    Console.WriteLine($"{error.Kind} at '{error.PropertyPath}': {error.Details}");
}
```

## Error handling

`CheckSchemaCompatibility` returns `Result<SchemaVersionId, CheckSchemaCompatibilityError>`. The error type is a variant that wraps an `Exception`:

```csharp
var result = await client.Registry.CheckSchemaCompatibility(
    SchemaName.From("order-placed"),
    proposedSchema,
    SchemaDataFormat.Json
);

result.Switch(
    versionId => Console.WriteLine($"Schema is compatible. Version: {versionId}"),
    error => {
        Console.WriteLine($"Compatibility check failed: {error.ErrorMessage}");
    }
);
```

For transforming the result:

```csharp
var message = result.Match(
    versionId => $"Compatible with version {versionId}",
    error => $"Incompatible: {error.ErrorMessage}"
);
```

## When to use each compatibility mode

| Scenario                           | Recommended Mode     | Rationale                                     |
|------------------------------------|----------------------|-----------------------------------------------|
| Consumers upgrade before producers | `Backward`           | New consumers must read old data.             |
| Producers upgrade before consumers | `Forward`            | Old consumers must read new data.             |
| Independent upgrade schedules      | `Full`               | Either side may upgrade first.                |
| Rapid prototyping / development    | `None`               | Breaking changes are expected and acceptable. |
| Shared events across many services | `Full`               | Minimizes coordination overhead.              |
| Internal single-service events     | `Backward` or `None` | Fewer consumers to coordinate.                |

## Workflow: evolving a schema safely

A typical workflow for evolving a schema with compatibility enforcement:

```csharp
// 1. Create the initial schema with backward compatibility
var createResult = await client.Registry.CreateSchema(
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
    description: "Order placed event",
    tags: new Dictionary<string, string> { ["domain"] = "orders" }
);

// 2. Later, propose a new version that adds an optional field
var proposedV2 = """
    {
        "type": "object",
        "properties": {
            "orderId": { "type": "string" },
            "product": { "type": "string" },
            "quantity": { "type": "integer" },
            "discount": { "type": "number" }
        },
        "required": ["orderId", "product", "quantity"]
    }
    """;

// 3. Check compatibility before registering
var checkResult = await client.Registry.CheckSchemaCompatibility(
    SchemaName.From("order-placed"),
    proposedV2,
    SchemaDataFormat.Json
);

// 4. Act on the result
checkResult.Switch(
    versionId => {
        Console.WriteLine($"Schema v2 is compatible. Safe to register.");
        // Proceed to register the new version
    },
    error => {
        Console.WriteLine($"Schema v2 is NOT compatible: {error.ErrorMessage}");
        // Fix the schema before attempting to register
    }
);
```

Adding an optional field (`discount`) without changing the `required` array is backward compatible -- consumers using the old schema can still read events produced with the new schema because they simply ignore the new field.

If instead you tried to add `discount` to the `required` array, the compatibility check would fail with a `NewRequiredProperty` error, because existing data written without a `discount` field could not satisfy the new schema's requirements.
