---
order: 4
---

# Client options

<CopyMarkdownButton />

`KurrentClientOptions.Schema` controls how the client interacts with the registry on append: whether new schemas are auto-registered the first time the client sees them, and whether outgoing messages are validated against the registered schema before they leave the client. Set it once on the client and every module picks it up.

For serialization itself (data formats, type mapping, name strategies), see [Serialization](../serialization/README.md).

## Presets

Pick the preset that matches your environment.

| Preset                     | AutoRegister | PreValidate | Use when                                                       |
|----------------------------|--------------|-------------|----------------------------------------------------------------|
| `FullValidation` (default) | true         | true        | Development and most production setups.                        |
| `NoAutomaticRegistration`  | false        | true        | Schemas are registered out of band but messages must conform.  |
| `NoValidation`             | true         | false       | Auto-register without the validation overhead.                 |
| `Disabled`                 | false        | false       | Manual schema management.                                      |

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema = KurrentClientSchemaOptions.Disabled);
```

## Override individual settings

`KurrentClientSchemaOptions` also exposes `NameStrategy`, `Mapper`, `Json`, and `Protobuf`. Override one property on a preset with a `with`-expression.

```csharp
opts.Schema = KurrentClientSchemaOptions.NoAutomaticRegistration with {
    NameStrategy = SchemaNameStrategies.Namespace("acme")
};
```

The registration and validation behavior of the preset is preserved. Only the property you assign changes. See [Serialization](../serialization/README.md) for the available strategies and mapper APIs.
