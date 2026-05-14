---
order: 4
---

# Deleting a schema

<CopyMarkdownButton />

`DeleteSchema` removes a schema and every version it holds.

```csharp
await client.Registry.DeleteSchema("order-placed");
```

::: warning
Deletion is destructive. Existing consumers that hold a `SchemaVersionId` from this schema will start to receive `SchemaDeletedException` on subsequent reads. Coordinate with consumers before deleting a shared schema.
:::

## Errors

| Exception                       | When it fires                                |
|---------------------------------|----------------------------------------------|
| `SchemaNotFoundException`       | The schema does not exist.                   |
| `SchemaDeletedException`        | The schema is already deleted.               |
