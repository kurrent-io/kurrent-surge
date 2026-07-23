# Kurrent .NET SDK

> .NET monorepo SDK for the Kurrent platform — independently installable gRPC client and stream processing packages.

This file documents only what breaks convention. If a senior developer would guess it correctly from the codebase, it
does not belong here. Do not add standard patterns, paraphrased docs, or facts discoverable from a single config file.
When editing, preserve this bar.

## Stack

Targets net10.0 only. Use the latest net10 APIs. Source generators are the exception — they target
netstandard2.0 (Roslyn requirement). Never pass `--framework` to solution-wide `dotnet build` or `dotnet test`
— it forces a single TFM onto the whole solution and breaks the source generator projects with `NETSDK1005`.

C# 14 extension members (`extension(Type t)`) are in active use in the Streams module.

No meta-package — consumers install only what they need.

Greenfield project: no legacy compatibility constraints, no existing user base.

## Build & Test

Test containers are managed by Aspire (`test/Aspire.Hosting.KurrentDB`). TLS certificates are generated
automatically by the test harness — no manual setup required.

When consolidating multiple experimental versions into a single implementation, do not delete the old files
until the new version builds and its tests pass.

When a failing test reveals a mismatch between test expectation and code output (e.g. test expects snake_case,
code produces kebab-case), do not unilaterally change either side. Report the mismatch, offer both options,
and wait for direction. Heuristics like "majority of call sites" are not permission to decide.

Before referring to any project, file, or path by name — verify it exists on disk. Do not assume sibling
projects exist from naming convention alone.

Playground, probe, and throwaway projects (`playground/`, `.claude/context/.scratch/`) always target net10.0 — they
are not consumer-facing, so the minimum-supported TFM rule does not apply.

When debugging CI or workflow issues, create a minimal throwaway workflow targeting just the failing step
instead of running the full pipeline.

`tools/` contains .NET 10 file-based apps (`.cs` files with `#:` directives). Restore once after cloning: `dotnet tool restore`.

| Method     | Example                                    | Notes                          |
|------------|--------------------------------------------|--------------------------------|
| Local tool | `dotnet semver-bump check`                 | Requires `dotnet tool restore` |
| dnx        | `dnx semver-bump check`                    | No install needed              |
| Script     | `dotnet run tools/semver-bump.cs -- check` | Runs from source directly      |

## Protocols

Dual gRPC split — V2 handles appends, registry, indexes, and connectors; V1 legacy handles reads and management.
The V2 read path is commented out; those RPCs do not exist in the proto yet. Proto definitions live in
`proto/kurrentdb/protocol/{v0,v1,v2}/`.

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
Kurrent.CodeAnalysis                       ← shared Roslyn infrastructure for source generators
```

`Kurrent.Client` contains a `Legacy/` folder with V1 gRPC compatibility — not a separate package.

Streams module split across 15+ partial class files. Naming convention documented in [Module Organization](.claude/context/project/module-organization.md).

Sum types use source-generated `IVariant` discriminated unions. Exception types decorated with
`[KurrentOperationError]` get source-generated constructors.

## Project context

Knowledge base at `.claude/context/` (see Knowledge Base system prompt for structure and rules).
Plan-mode scratch at `.claude/plans/`. Throwaway files at `.claude/context/.scratch/`.

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                                              | When to read                                             |
|-----------------------------------------------------------------------|----------------------------------------------------------|
| [Architecture](.claude/context/project/architecture.md)               | Before structural changes, new modules, or protocol work |
| [Kurrent.Client](.claude/context/project/client.md)                   | Before working on the client library                     |
| [Builder Conventions](.claude/context/project/builder-conventions.md) | Before adding builders, factories, or fluent APIs        |
| [Packaging](.claude/context/project/packaging.md)                     | Before adding project references to packable projects    |
| [XML Documentation](.claude/context/project/xmldoc.md)                | Before writing or reviewing XML docs on public APIs      |
| [Module Organization](.claude/context/project/module-organization.md) | Before adding files, models, or mappers to a module      |
| [Docs Guidelines](.claude/context/project/docs-guidelines.md)         | Before writing or updating documentation                 |

## Agent skills

### Issue tracker

Issues live in **Linear** — DevEx team, `Kurrent SDK .NET` project, referenced as `DEV-XXXX` and operated
through the Linear MCP. PRs are not a request surface.

The full workflow lives in `.claude/context/project/issue-tracker-linear.md` and the `devx-linear-workflow` 
skill (MUST be consulted before any Linear operation). See `.agents/matt-pocock/issue-tracker.md`.

### Triage labels

The five canonical triage roles map onto Linear's native workflow states plus the classification labels in
`devx-linear-workflow`; the role strings are not configured labels and issue status is never set manually.
See `.agents/matt-pocock/triage-labels.md`.

### Domain docs

- Single-context: `CONTEXT.md` at the repo root, 
- Existing reference docs under `.claude/context/project/`.
- ADRs are not part of our doc workflow, but some skills under evaluation emit them — if one is ever created it goes in `.claude/context/docs/adr/`, reserved so those skills work.

See `.agents/matt-pocock/domain.md`.
