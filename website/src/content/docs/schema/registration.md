---
title: Registration
description: One dial with three presets. Off, automatic, or strict.
---

Registration decides whether Kurrent Surge puts your schemas into the registry, and validation decides
whether it checks them before use. One options object carries both.

## The presets

| Preset | Registration | Validation | Use when |
|---|---|---|---|
| `Disabled` | manual | off | **default**. The registry is not in your path |
| `AutoRegister` | automatic | off | development, and any service that owns its schemas |
| `Strict` | manual | on | production consumers that must fail fast on drift |

```csharp
builder.Services.AddKurrentSurge(surge => surge
    .Connection("kurrentdb://localhost:2113", opts =>
        opts.Schema = KurrentClientSchemaOptions.AutoRegister));
```

Outside DI, set it the same way on `KurrentClient.Create`:

```csharp
await using var client = KurrentClient.Create(
    "kurrentdb://localhost:2113",
    opts => opts.Schema = KurrentClientSchemaOptions.Strict);
```

## What each does

**Disabled** touches the registry for nothing. Schema names still resolve through your catalog, so
append and read work. The registry is not consulted at all.

**AutoRegister** walks your catalogs when the client initializes and registers every schema that is
not there. A schema that already exists is left alone. Nothing is validated, because you are the
one defining the schemas.

**Strict** registers nothing and requires every catalog schema to be present and compatible before
the client will use it. An unregistered type, or one whose shape has drifted from the registered
version, fails at initialization rather than at the first append.

## The pieces

`RegistrationMode` takes `SchemaRegistrationMode.Manual` or `.Auto`. `ValidationMode` takes
`SchemaValidationMode.Disabled` or `.Enabled`. `NameStrategy` decides how a schema name is derived
when the type does not pin one. `AppendAsJson` appends records that state no format as JSON instead
of taking the format from the message type. The three presets are just combinations of the first
two, and you can set every member individually.

## What gets registered

Whatever the wire format, the definition sent to the registry is a **JSON Schema exported from your
CLR type**. A Protobuf-encoded or Avro-encoded record registers the same kind of definition a JSON
one does.

That is what lets [compatibility modes](/kurrent-surge/schema/evolution/) work uniformly: one comparable shape per
schema name, regardless of encoding. The exception is `Bytes`, which never reaches the registry at
all. See [Data Formats](/kurrent-surge/schema/formats/).

## Timing

The catalog walk runs once, single-flight, on the client's initialization path. Every caller shares
the same task, faults included. A registration failure therefore surfaces on the first operation
rather than being retried per record.

:::note
Registry reads are served by a projection that lags writes by a few hundred milliseconds. The SDK
accounts for this: a create that reports the schema already exists is authoritative, and a read that
has not caught up is retried rather than treated as an absence.
:::
