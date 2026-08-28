---
title: Registration
description: One dial with three presets — off, automatic, or strict.
---

Registration decides whether the SDK puts your schemas into the registry, and validation decides
whether it checks them before use. One options object carries both.

## The presets

| Preset | Registration | Validation | Use when |
|---|---|---|---|
| `Disabled` | manual | off | **default** — the registry is not in your path |
| `AutoRegister` | automatic | off | development, and any service that owns its schemas |
| `Strict` | manual | on | production consumers that must fail fast on drift |

```csharp
builder.Services.AddKurrent(kurrent => kurrent
    .Connection("kurrentdb://localhost:2113")
    .Schema(KurrentClientSchemaOptions.AutoRegister));
```

## What each does

**Disabled** touches the registry for nothing. Schema names still resolve through your catalog, so
append and read work — the registry simply is not consulted.

**AutoRegister** walks your catalogs when the client initializes and registers every schema that is
not there. A schema that already exists is left alone. Nothing is validated, because you are the
one defining the schemas.

**Strict** registers nothing and requires every catalog schema to be present and compatible before
the client will use it. An unregistered type, or one whose shape has drifted from the registered
version, fails at initialization rather than at the first append.

## The pieces

`RegistrationMode` is `Manual` or `Auto`. `ValidationMode` is `Disabled` or `Enabled`. `NameStrategy`
decides how a schema name is derived when the type does not pin one. The three presets are just
combinations, and you can set them individually.

## Timing

The catalog walk runs once, single-flight, on the client's initialization path. Every caller shares
the same task, faults included — so a registration failure surfaces on the first operation rather
than being retried per record.

:::note
Registry reads are served by a projection that lags writes by a few hundred milliseconds. The SDK
accounts for this: a create that reports the schema already exists is authoritative, and a read that
has not caught up is retried rather than treated as an absence.
:::
