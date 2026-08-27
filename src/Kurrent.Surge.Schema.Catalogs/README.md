# Kurrent.Surge.Schema.Catalogs

The message **declaration layer** for the Kurrent Platform — extracted so contracts/domain
assemblies can declare their messages without dragging in gRPC, Google.Protobuf, or the client.

The Kurrent client references this same package, so both sides speak the same types.

## Installation

```shell
dotnet add package Kurrent.Surge.Schema.Catalogs
```

`Kurrent.Surge` references this package. Add it only to a contracts or domain assembly that must
declare its messages without the client.

### Supported frameworks

- .NET 10.0 or a subsequent version

## What it gives you

| Concern                            | API                                                                                  |
|------------------------------------|--------------------------------------------------------------------------------------|
| Per-type declaration               | `[MessageType<T>("wire-name")]` / `[MessageType<T, TStrategy>]`                      |
| Group declaration (namespace scan) | `[assembly: MessageCatalog("My.Namespace", Key = "...")]`                            |
| Hand-written catalog               | `[CustomMessageCatalog]` on a `MessageCatalog` subclass                              |
| Schema vocabulary                  | `SchemaName`, `SchemaDataFormat` (`Kurrent.Surge.Schema`)                            |
| Naming strategies                  | `ISchemaNameStrategy`, `SchemaNameStrategies`, `SchemaUrn` (`Kurrent.Surge.Schema.Naming`) |

A source generator (shipped inside this package as an analyzer) turns those declarations into a
discoverable `MessageCatalog` per assembly, with zero runtime reflection — fully NativeAOT-compatible.

## Requirement

Contracts projects that reference this package **must target `net10.0`** — both to reference the
package and because the generated code uses current C# features.

## Namespaces

- `Kurrent.Surge.Schema` — shared schema vocabulary (`SchemaName`, `SchemaDataFormat`)
- `Kurrent.Surge.Schema.Catalogs` — the declaration layer (attributes, `MessageCatalog`, `MessageResolver`)
- `Kurrent.Surge.Schema.Naming` — schema naming (`ISchemaNameStrategy`, strategies, `SchemaUrn`)
