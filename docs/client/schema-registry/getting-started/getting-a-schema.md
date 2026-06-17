---
order: 2
---

# Getting a schema

<CopyMarkdownButton />

Three read operations cover the common queries:

| Operation             | Returns                              | Use when                                                        |
|-----------------------|--------------------------------------|-----------------------------------------------------------------|
| `GetSchema`           | Metadata (no definition)             | Building admin views, inspecting compatibility, listing tags.   |
| `GetSchemaVersion`    | A version including its definition   | You need the actual schema body for the latest or a numbered version. |
| `GetSchemaVersionById`| A version by `SchemaVersionId`       | You held on to the id from a previous registration.             |
| `ListSchemas`         | A filtered list of schemas           | Discovery by name prefix or tags.                               |

## Get schema metadata

`GetSchema` returns name, configuration, and timestamps but not the schema body. Use `GetSchemaVersion` when you need the definition itself.

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

`GetSchemaVersion` returns a version including its definition. It accepts either the latest or a specific version number.

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

`GetSchemaVersionById` is the sibling for when you have a `SchemaVersionId`, for example from a `CreateSchema` result or a stored consumer binding.

All three throw `SchemaNotFoundException` for unknown names or numbers, or `SchemaDeletedException` if the parent schema was deleted.

## List schemas

`ListSchemas` enumerates registered schemas, optionally filtered by name prefix and tags. Tag filters require every specified tag to match. Create a `ListSchemasOptions` to combine filters. The call returns `IReadOnlyList<Schema>`.

```csharp
var schemas = await client.Registry.ListSchemas(new ListSchemasOptions {
    SchemaNamePrefix = "order-",
    Tags             = new Dictionary<string, string> { ["team"] = "fulfillment" }
});
```

| Property           | Type                                  | Description                                                 |
|--------------------|---------------------------------------|-------------------------------------------------------------|
| `SchemaNamePrefix` | `string`                              | Filter by schema name prefix. Empty means no prefix filter. |
| `Tags`             | `IDictionary<string, string>`         | Tag filter. Empty means no tag filter.                      |

A request with no filters returns every registered schema.

::: tip
For single-filter queries, overloads cover the common shapes:

```csharp
var all          = await client.Registry.ListSchemas();
var orders       = await client.Registry.ListSchemas(schemaNamePrefix: "order-");
var fulfillment  = await client.Registry.ListSchemas(
    tags: new Dictionary<string, string> { ["team"] = "fulfillment" });
```
:::
