---
title: Schema Registry
index: false
---

# Schema Registry

<CopyMarkdownButton />

The schema registry stores reusable schema definitions and validates new versions against compatibility rules. Use it to coordinate event evolution across producers and consumers without breaking either side. All operations live on `client.Registry`.

## In this section

- [How it works](./how-it-works.md) — what the registry stores, how validation runs on append, where the pieces live.
- [Getting started](./getting-started/README.md) — practical guides for creating, getting, updating, and deleting schemas.
- [Compatibility modes](./compatibility-modes.md) — evolution rules, mode trade-offs, and the error model.
- [Client options](./client-options.md) — auto-registration and pre-validation behavior at the client level.

For the encode and decode side (data formats, type mapping, name strategies), see [Serialization](../serialization/README.md).

## Quick example

Create a schema with backward compatibility, then register a new version:

```csharp
var v1 = """
    {
        "type": "object",
        "properties": {
            "orderId": { "type": "string" },
            "product": { "type": "string" }
        },
        "required": ["orderId", "product"]
    }
    """;

await client.Registry.CreateSchema(
    "order-placed", v1, SchemaDataFormat.Json, CompatibilityMode.Backward);

var v2 = await client.Registry.RegisterSchemaVersion("order-placed", updatedJson);

Console.WriteLine("Registered version {0}", v2.VersionNumber);
```

The full walkthrough lives in [Getting started](./getting-started/README.md).
