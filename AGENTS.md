# Kurrent .NET SDK

> .NET monorepo SDK for the Kurrent platform — independently installable gRPC client and stream processing packages.

This file documents only what breaks convention. If a senior developer would guess it correctly from the codebase, it
does not belong here. Do not add standard patterns, paraphrased docs, or facts discoverable from a single config file.
When editing, preserve this bar.

## Stack

Targets net10.0 only. Use the latest net10 APIs. Source generators are the exception — they target netstandard2.0.

C# 14 extension members (`extension(Type t)`) are in active use in the Streams module.

No meta-package — consumers install only what they need.

Greenfield project: no legacy compatibility constraints, no existing user base.

## Build & Test

Test containers are managed by Aspire (`test/Aspire.Hosting.KurrentDB`). TLS certificates are generated
automatically by the test harness — no manual setup required.

`tools/` contains .NET 10 file-based apps (`.cs` files with `#:` directives). Restore once after cloning: `dotnet tool restore`.

| Method     | Example                                    | Notes                          |
|------------|--------------------------------------------|--------------------------------|
| Local tool | `dotnet semver-bump check`                 | Requires `dotnet tool restore` |
| dnx        | `dnx semver-bump check`                    | No install needed              |
| Script     | `dotnet run tools/semver-bump.cs -- check` | Runs from source directly      |

## Protocols

Dual gRPC split — V2 handles appends, registry, indexes, and connectors; V1 legacy handles reads and management. The V2 read path is commented out; those RPCs do not exist in the proto yet. Proto definitions live in `proto/kurrentdb/protocol/{v0,v1,v2}/`.

## Structure

Publishable packages:

```
Kurrent.Client              ← gRPC client (streams, registry, connectors, admin)
  └── Kurrent.Streaming     ← stream processing, depends on Kurrent.Client
```

Solution-level shared libraries (not published as NuGet packages):

```
Variant / Variant.SourceGenerators         ← discriminated union source generator
Kurrent.Client.SourceGenerators            ← client-specific source generators
Kurrent.CodeAnalysis                       ← Roslyn analyzers
Kurrent.Toolkit                            ← shared internal utilities
ProtobufTranscoder                         ← protobuf wire-format transcoding (experimental and not in the solution)
```

`Kurrent.Client` contains a `Legacy/` folder with V1 gRPC compatibility — not a separate package.

Streams module split across 15+ partial class files. Naming convention: `{Module}Client.{Operation}.cs`, `.Models.cs`, `.Mappers.cs`, `.Extensions.cs`, `.Models.Builders.cs`.

Sum types use source-generated `IVariant` discriminated unions. Exception types decorated with `[KurrentOperationError]` get source-generated constructors.

## Project context

Knowledge base at `.claude/context/` (see Knowledge Base system prompt for structure and rules).
Plan-mode scratch at `.claude/plans/`. Throwaway files at `.project/.scratch/`.

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                                              | When to read                                             |
|-----------------------------------------------------------------------|----------------------------------------------------------|
| [Architecture](.claude/context/project/architecture.md)               | Before structural changes, new modules, or protocol work |
| [Kurrent.Client](.claude/context/project/client.md)                   | Before working on the client library                     |
| [Builder Conventions](.claude/context/project/builder-conventions.md) | Before adding builders, factories, or fluent APIs        |
| [Versioning](.claude/context/project/versioning.md)                   | Before computing versions or publishing packages         |
