---
order: 1
---

# How it works

<CopyMarkdownButton />

The schema registry stores reusable schema definitions and enforces evolution rules across producers and consumers. Producers register the shape of their events once. Consumers reference the same schema to decode events safely. When a producer wants to change the shape, the registry checks the change against a compatibility mode before letting it through.

## What the registry stores

A schema has a unique name, a data format, a compatibility mode, and an ordered list of versions.

| Element             | Description                                                                                |
|---------------------|--------------------------------------------------------------------------------------------|
| Name                | Stable identifier used by producers and consumers. Unique per registry.                    |
| Data format         | How the definition is encoded. `Json`, `Protobuf`, `Avro`, or `Bytes`.                     |
| Compatibility mode  | Rule applied when a new version is registered. See [Compatibility modes](./compatibility-modes.md). |
| Versions            | The historical definitions. Each one has a numeric `VersionNumber` and a `VersionId`.      |
| Description, tags   | Optional metadata for search and discovery.                                                |

Every successful registration produces a `SchemaVersionDescriptor` carrying the new `VersionId` and `VersionNumber`. Code that pins to a specific contract should hold the id, not the number.

## How validation works on append

`KurrentClientOptions.Schema` ties the client to the registry. Two settings drive its behaviour at append time.

| Setting        | When it fires                  | Effect                                                                                    |
|----------------|--------------------------------|-------------------------------------------------------------------------------------------|
| `AutoRegister` | First time a type is appended  | Registers a schema derived from the message type.                                         |
| `PreValidate`  | Every append                   | Validates the outgoing payload against the registered schema before the call leaves the client. |

Both default to `true`. The presets in [Client options](./client-options.md) cover the common combinations.

The lifecycle of a single append, with both options on, looks like this:

1. The client computes the schema name for the message type using the configured name strategy.
2. If `AutoRegister` is on and no schema with that name exists yet, the client calls `CreateSchema`.
3. If `PreValidate` is on, the client serializes the payload and validates it against the current schema definition.
4. The payload is sent to the server.

Steps 2 and 3 are skipped when the corresponding option is off. Step 1 always runs because every event carries a schema name on the wire.

## Schema evolution

Once a schema is created, every later version is checked against its compatibility mode. The check happens server-side. Incompatible changes fail with `SchemaIncompatibleException` and the call lists the violations.

Producers and consumers usually upgrade on different timelines. The compatibility mode picks which side may move first. See [Compatibility modes](./compatibility-modes.md) for the rules and how to choose one.

To validate a proposed change without registering it, use `CheckSchemaCompatibility`. The check is identical to the one that runs on `RegisterSchemaVersion`, but it makes no change to the registry.

## Where things live in the SDK

| API                                  | Lives on               |
|--------------------------------------|------------------------|
| `CreateSchema`, `DeleteSchema`, version operations | `client.Registry`      |
| Encoder, decoder, name strategies    | `KurrentClientOptions.Schema` and [Serialization](../serialization/README.md) |
| Auto-register and pre-validate flags | `KurrentClientOptions.Schema` |

The registry module is independent of serialization. The serializer turns a .NET type into bytes and a schema definition. The registry stores those definitions and policies their evolution. You can use either piece without the other.
