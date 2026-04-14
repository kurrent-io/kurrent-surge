# Kurrent .NET SDK

> .NET monorepo SDK for the Kurrent platform — independently installable gRPC client and stream processing packages.

This file documents only what breaks convention. If a senior developer would guess it correctly from the codebase, it
does not belong here. Do not add standard patterns, paraphrased docs, or facts discoverable from a single config file.
When editing, preserve this bar.

## Stack

Multi-target: net8.0, net9.0, net10.0. Use only net8.0 APIs unless behind `#if` guards; build with `--framework net8.0` to verify.

C# 14 extension members (`extension(Type t)`) are in active use in the Streams module.

No meta-package — consumers install only what they need.

Greenfield project: no legacy compatibility constraints, no existing user base.

## NuGet Feed

`KurrentDB.*` packages are sourced from a private GitHub Packages feed at `nuget.pkg.github.com/kurrent-io`. The `nuget.config` at the repo root adds this as the `GitHub` source. Local `dotnet restore` requires credentials — authenticate via `dotnet nuget add source` or `~/.nuget/NuGet/NuGet.Config` with a PAT that has `read:packages` scope.

## Build & Test

Read the [Testing](.agents/context/testing.md) section for more details.

One-time setup: `sudo ./scripts/gencert.sh` generates TLS certs for secure test mode. FluentDocker containers (not TestContainers) are reused across runs.

`tools/` contains .NET 10 file-based apps (`.cs` files with `#:` directives). Restore once after cloning: `dotnet tool restore`.

`KurrentDB.*` packages are sourced from a private GitHub Packages feed (`nuget.pkg.github.com/kurrent-io`). Local `dotnet restore` requires credentials with `read:packages` scope.

| Method     | Example                                    | Notes                          |
|------------|--------------------------------------------|--------------------------------|
| Local tool | `dotnet semver-bump check`                 | Requires `dotnet tool restore` |
| dnx        | `dnx semver-bump check`                    | No install needed              |
| Script     | `dotnet run tools/semver-bump.cs -- check` | Runs from source directly      |

## Protocols

Dual gRPC split — V2 handles appends, registry, indexes, and connectors; V1 legacy handles reads and management. The V2 read path is commented out; those RPCs do not exist in the proto yet. Proto definitions live in `proto/kurrentdb/protocol/v1/` and `proto/kurrentdb/protocol/v2/`.

Before editing any `.proto` file, load the `/proto-style` skill. Never remove or renumber existing fields.

## Structure

Layer hierarchy (each package is independently installable):

```
Kurrent.Client              ← foundation, no SDK dependencies
  └── Kurrent.Processors    ← stream processing, depends on Kurrent.Client
        └── Kurrent.Connectors.*  ← planned
Kurrent.Framework.*         ← future, depends on Kurrent.Client
```

`KurrentDB.Client` (v1) is a separate legacy package. `Kurrent.Client` (v2) is a clean break with zero backward compatibility.

Streams module split across 15+ partial class files. Naming convention: `{Module}Client.{Operation}.cs`, `.Models.cs`, `.Mappers.cs`, `.Extensions.cs`, `.Models.Builders.cs`.

Sum types use source-generated `IVariant` discriminated unions. Exception types decorated with `[KurrentOperationError]` get source-generated constructors.

## Project context

```text
.project/
├── .scratch/                    # throwaway files; never promote to permanent docs
└── docs/                        # all documentation; use typed subdirectories only
    ├── specs/                   # design documents (YYYY-MM-DD-<topic>-design.md)
    ├── plans/                   # implementation plans (YYYY-MM-DD-<feature-name>.md)
    └── research/                # research findings (YYYY-MM-DD-<topic>-research.md)

.agents/
└── context/                     # topic reference files; read on demand, not upfront
```

All docs must be dated and named after their topic. Files in `docs/` go in a typed subdirectory; never in `docs/` directly.

TRIPWIRE: If you find yourself writing a doc into the wrong subdirectory, move it immediately.

TRIPWIRE: If you find yourself writing a doc inline in conversation, stop and write it to `.project/docs/`.

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                                | When to read                                             |
|---------------------------------------------------------|----------------------------------------------------------|
| [Architecture](.agents/context/architecture.md)         | Before structural changes, new modules, or protocol work |
| [Code Conventions](.agents/context/code-conventions.md) | Before writing new code in unfamiliar areas              |
| [Kurrent.Client](.agents/context/client.md)             | Before working on the client library                     |
| [Kurrent.Processors](.agents/context/processors.md)     | Before working on stream processing                      |
| [Testing](.agents/context/testing.md)                   | Before writing or modifying tests                        |
| [Versioning](.agents/context/versioning.md)             | Before computing versions or publishing packages         |
