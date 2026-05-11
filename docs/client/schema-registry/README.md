---
title: Schema Registry
index: false
---

# Schema Registry

<CopyMarkdownButton />

The schema registry stores reusable schema definitions and validates new versions against compatibility rules. Use it to coordinate event evolution across producers and consumers without breaking either side.

All operations live on `client.Registry`.

## Concepts

A **schema** has a name, a data format (`Json`, `Protobuf`, `Avro`, `Bytes`), a compatibility mode, and one or more versions. Each **version** has its own id and definition. Once a schema is created, future versions must satisfy its compatibility mode (Backward, Forward, Full, etc.).

The client interacts with the registry on append:

- **Auto-register** schemas the first time they are seen.
- **Pre-validate** outgoing messages against the registered schema.

Both are on by default. See [Client options](./client-options.md) to change that behavior.

## Quick example

Create a schema with backward compatibility:

```csharp
var schemaJson = """
    {
        "type": "object",
        "properties": {
            "orderId": { "type": "string" },
            "product": { "type": "string" }
        },
        "required": ["orderId", "product"]
    }
    """;

var descriptor = await client.Registry.CreateSchema(
    "order-placed", schemaJson, SchemaDataFormat.Json, CompatibilityMode.Backward);

Console.WriteLine("Created version {0}", descriptor.VersionNumber);
```

## In this section

- [Managing schemas](./managing-schemas.md) — create, get, list, register versions, delete.
- [Compatibility](./compatibility.md) — evolution rules and the `CheckSchemaCompatibility` operation.
- [Client options](./client-options.md) — auto-registration and pre-validation behavior.

For the encode/decode side (data formats, type mapping, name strategies), see [Serialization](../serialization.md).
