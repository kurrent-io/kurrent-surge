---
order: 3
---

# Compatibility modes

<CopyMarkdownButton />

Each schema can have multiple versions. Versioning is governed by a compatibility mode applied to the schema. Requests to register new versions are checked against this mode by the registry before they can succeed.

Compatibility modes control how schemas can or cannot evolve over time. These modes form the contract between applications producing and consuming data. When a new version of a schema is submitted to the registry, the mode applied to the schema name decides whether the new version is accepted.

There are seven compatibility modes: `None`, `Backward`, `BackwardAll`, `Forward`, `ForwardAll`, `Full`, `FullAll`.

Pick a mode when you create the schema. The registry validates each later version against it. For the operational side, see [Adding a schema version](./getting-started/updating-a-schema/adding-a-version.md) and [Checking compatibility](./getting-started/updating-a-schema/checking-compatibility.md).

## Required and optional in JSON Schema

The current implementation validates JSON Schema definitions. A property is required when its name appears in the schema's top-level `required` array. A property is optional otherwise. Adding or removing required-ness is the core operation that compatibility modes police.

The examples below all use a JSON Schema for an `order-placed` event:

```json
{
    "type": "object",
    "properties": {
        "orderId":  { "type": "string" },
        "product":  { "type": "string" },
        "quantity": { "type": "integer" }
    },
    "required": ["orderId", "product"]
}
```

In this baseline, `orderId` and `product` are required. `quantity` is optional.

Protobuf and Bytes definitions are stored but not validated server-side today. The modes still apply to JSON.

## None

No compatibility mode applies. Use this in development or when you do not yet know which mode fits. Any new version is accepted without a compatibility check.

```csharp
await client.Registry.CreateSchema(
    "internal-debug-event", schemaJson, SchemaDataFormat.Json, CompatibilityMode.None);
```

## Backward

This mode allows consumers to read both the current and the previous schema version. Use it to check compatibility against the previous schema version when you delete optional fields, add optional fields, or remove fields that had defaults. A typical use case for `Backward` is when your application has been built against the most recent schema and still needs to process old events.

`Backward` is the recommended starting point for most event-sourced systems.

For example, assume the `order-placed` schema starts with `orderId` (required), `product` (required), and `quantity` (optional).

If your next version adds an optional `customerId` property without changing the `required` array, this would successfully register. `Backward` compatibility requires consumers to read both the current and previous versions. Your consumers on the new schema can still read old events because the new property is optional and absent on existing records.

If your next version instead adds `customerId` to the `required` array, this would not successfully register with `Backward` compatibility. Existing events do not carry `customerId`, so consumers on the new schema could not read old events that satisfy only the previous version. The registration fails with a `NewRequiredProperty` error.

Removing the optional `quantity` field is also backward compatible. Removing the required `orderId` field is not, because data written under the previous version still includes it and the new schema can no longer describe that data. The registration fails with a `MissingRequiredProperty` error.

## BackwardAll

This mode allows consumers to read the current schema and every previous version. Use it to check compatibility against all previous versions when you delete optional fields or add optional fields. Suitable for long-lived event logs where consumers replay from version one.

The rules per change are the same as `Backward`. The difference is that the check runs against every prior version, not just the immediately preceding one.

## Forward

This mode allows consumers to read both the current and the subsequent schema version, but not necessarily later versions. Use it to check compatibility against the last schema version when you add fields or delete optional fields. A typical use case for `Forward` is when your application is built against a previous schema and must keep processing events produced under a newer schema.

For example, assume the `order-placed` schema is `orderId` (required), `product` (required), `quantity` (optional).

If your next version adds a required `customerId` property, this would successfully register. `Forward` compatibility requires consumers on the previous version to read data produced under the new schema. They can do so because the extra `customerId` field on new events is ignored by the previous schema.

If your next version instead removes the required `orderId` property, this would not successfully register with `Forward` compatibility. Consumers on the previous version expect `orderId` and cannot interpret new events that omit it. The registration fails with a `RemovedProperty` error.

Removing the optional `quantity` field while leaving the `required` array unchanged is forward compatible. Old consumers ignore extra fields, and removing an optional field does not break them. Removing it while adding a different required field is also valid as long as the new required field already existed in some optional form prior, otherwise the older consumer cannot handle the new shape.

## ForwardAll

This mode allows consumers on any previously registered version to read data produced under any later version. Use it when you need to add fields or delete optional fields and check compatibility against every previous version.

The rules per change are the same as `Forward`, applied against every prior version rather than just the most recent.

## Full

This mode allows consumers to read data produced by the previous and the next version of the schema, but not earlier or later versions. Use it to check compatibility against the last schema version when you add or remove optional fields. Both producers and consumers may upgrade independently as long as no version is more than one step apart.

In practice, `Full` permits only additions or removals of optional fields. Required fields cannot be added (would break `Forward`) or removed (would break `Backward`). Type changes are not permitted.

## FullAll

This mode allows consumers using any previous schema version to read data written under any later version, and vice versa. Use it when you need the strongest guarantee across an entire schema history.

The rules per change are the same as `Full`, applied against every prior version. This is the most restrictive mode and the safest contract for events shared across many independent services.

## Reference

The seven modes in one table. The numeric values correspond to the `CompatibilityMode` enum on the wire.

| Mode          | Value | Reads previous | Read by previous | Applied against        |
|---------------|-------|----------------|------------------|------------------------|
| `None`        | 7     | n/a            | n/a              | No check               |
| `Backward`    | 1     | Yes            | No               | Previous version       |
| `BackwardAll` | 4     | Yes            | No               | Every previous version |
| `Forward`     | 2     | No             | Yes              | Previous version       |
| `ForwardAll`  | 5     | No             | Yes              | Every previous version |
| `Full`        | 3     | Yes            | Yes              | Previous version       |
| `FullAll`     | 6     | Yes            | Yes              | Every previous version |

`Unspecified` (0) is the proto default and should not be used.

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

## Compatibility errors

`SchemaIncompatibleException.Errors` is a `SchemaCompatibilityErrors` collection. Each entry is a `SchemaCompatibilityError` describing one incompatibility.

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

## Data formats

`SchemaDataFormat` specifies the serialization format for schema definitions.

| Value      | Description                       | Content type                          |
|------------|-----------------------------------|---------------------------------------|
| `Json`     | JSON Schema                       | `application/json`                    |
| `Protobuf` | Protocol Buffers                  | `application/vnd.google.protobuf`     |
| `Avro`     | Apache Avro                       | `application/vnd.apache.avro+json`    |
| `Bytes`    | Raw bytes (no schema validation)  | `application/octet-stream`            |

The data format is fixed at schema creation. `CheckSchemaCompatibility` and `RegisterSchemaVersion` reject mismatched formats with `DataFormatMismatch`.
