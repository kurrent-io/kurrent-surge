---
title: Registry
description: Manage schemas and their versions directly.
---

Most applications never call these — [registration](/schema/registration/) handles the registry for
you. Reach for this module when you manage schemas explicitly.

```csharp
ValueTask<SchemaVersionDescriptor> CreateSchema(SchemaName, Action<CreateSchemaOptions>?, CancellationToken)
ValueTask<SchemaVersionDescriptor> RegisterSchemaVersion(...)
ValueTask<Schema>                  GetSchema(...)
ValueTask<SchemaVersion>           GetSchemaVersion(...)
ValueTask<SchemaVersion>           GetSchemaVersionById(...)
ValueTask<IReadOnlyList<Schema>>   ListSchemas(...)
ValueTask                          DeleteSchema(...)
ValueTask<SchemaVersionId>         CheckSchemaCompatibility(...)
```

## Create versus register

`CreateSchema` makes a new schema. It fails if the name is taken.

`RegisterSchemaVersion` adds a version to a schema that exists. It fails if the definition has not
changed, and — when a compatibility mode is set — if the new version is not compatible with the last
one.

Together they are the evolution path: create once, register a version per change.

## Compatibility

`CheckSchemaCompatibility` asks whether a definition would be accepted, without writing anything.
That is what `Strict` validation uses to fail at initialization instead of at the first append.

## Consistency

The registry is CQRS. Commands — create, register, delete — fold the schema's own stream and are
strongly consistent. Queries — get, list, check — read a projection that lags writes by a few
hundred milliseconds.

That matters if you write then immediately read:

```csharp
await client.Registry.CreateSchema(name, …);
var version = await client.Registry.GetSchemaVersion(name);   // may not see it yet
```

`CreateSchema` returns the version id directly, so use its return value rather than reading it back.
