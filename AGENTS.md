# Kurrent .NET SDK

> .NET monorepo SDK for the Kurrent platform — independently installable gRPC client and stream processing packages.

This file documents only what breaks convention. If a senior developer would guess it correctly from the codebase, it
does not belong here. Do not add standard patterns, paraphrased docs, or facts discoverable from a single config file.
When editing, preserve this bar.

## Working rules

Earned the hard way. Each one cost a session.

- **Read the reference before designing.** When a site, repo, or product is named as the thing to
  match, read its source config first. Clone it if needed. Never reverse-engineer it from a
  screenshot — the answer is usually a dozen lines someone already wrote.
- **Check the skills list before the first edit in a domain**, not after failing. Same for the
  framework's own docs and its source in `node_modules`.
- **Solve the stated problem.** Do not invent requirements, and do not build a mechanism beside the
  framework when configuring the framework would do.
- **UI changes are verified in a browser**, by measuring the rendered elements. A green build, a
  passing grep, or correct markup in `dist/` is not verification.
- **A probe returning "missing" is unproven** until it shows it queried the right element. Return a
  control value beside the value under test. Never change code on a negative result alone.
- **One environment, named once.** Confirm which URL and which server the reader is on before
  reporting anything about it.
- **An earlier recommendation never outranks a current instruction.** If they conflict, say so in one
  line and follow the instruction.

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
are not consumer-facing, so the minimum-supported TFM rule does not apply. `playground/` is tracked and ships with
the repository; only `.claude/context/.scratch/` is throwaway.

When debugging CI or workflow issues, create a minimal throwaway workflow targeting just the failing step
instead of running the full pipeline.

`tools/` contains .NET 10 file-based apps (`.cs` files with `#:` directives). Restore once after cloning:
`dotnet tool restore`.

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

Two publishable packages:

```
Kurrent.Surge                  ← gRPC client (streams, registry, indexes, connectors, admin)
Kurrent.Surge.Core             ← contracts and shared foundation; message declaration layer; JSON, Protobuf, and Avro codecs; ships the Avro codec and catalog generators
```

Verify the live list with `grep -l "IsPackable>true" src/*/*.csproj`.

Solution-level shared libraries — `Variant` and `Kurrent.CodeAnalysis` — are not published as
NuGet packages. `Kurrent.Surge.Generators` (unique identifiers + the Avro codec generator) and
`Kurrent.Surge.Schema.Catalogs.Generators` are not packages either, but their DLLs ship inside
`Kurrent.Surge.Core` under `analyzers/dotnet/cs/`.

`Kurrent.Surge` contains a `Client/Legacy/` folder with V1 gRPC compatibility — not a separate package.

Streams module split across 13 partial class files. Naming convention documented in
[Module Organization](.claude/context/project/module-organization.md).

Sum types use source-generated `IVariant` discriminated unions.

### Two docs trees, one is dead

`website/` is the live documentation site, Astro and Starlight. `docs/` at the repo root is the
legacy VuePress KurrentDB product site and has nothing to do with the SDK. Reach for `website/`.
`.github/workflows/docs.yml` still filters on `docs/**`, so it never fires for the live site.

### Examples

Eighteen runnable projects under `examples/`, carried by `examples/Examples.slnx` and by
`Kurrent.Surge.slnx`. Read one before hand-writing a usage sample.

```
examples/streams/     QuickStart (31 lines, the minimal complete program), InventoryManagement
examples/processors/  WordCount, FraudDetection, IoTMonitoring, Leaderboard, OrderEnrichment,
                      ClickstreamSessions, WithPostgres, WithRedisCache, FullExample,
                      AspNetMinimal, Connector.Management
examples/registry/    Quickstart, OrderManagement
examples/             PackageTest, PackageTest.Streaming, Transfermarkt.Domain
```

No example covers indexes, the Avro codec, or the admin API.

`examples/README.md` is not an index. It documents message-catalog naming only and name-drops three
projects as illustrations. Every example carries its own README except the two `PackageTest`
projects and `Transfermarkt.Domain`, which are a package-resolution smoke test and a shared domain
library rather than usage samples.

CI builds them twice: once inside the root solution, then again as
`dotnet build examples/Examples.slnx -c Release -p:TestPackages=true`, which flips the `PackageTest`
projects from `ProjectReference` to a real `PackageReference` against the locally packed feed. A
failure in that second pass means the published package is broken, not the example.

## Project context

Knowledge base at `.claude/context/` — structure, frontmatter shapes, and rules live in the
`devx-knowledge-base` skill.
Plan-mode scratch at `.claude/plans/`. Throwaway files at `.claude/context/.scratch/`.

## Reference

Read these before working on related areas. Do not read all upfront.

| Document                                                              | When to read                                             |
|-----------------------------------------------------------------------|----------------------------------------------------------|
| [Architecture](.claude/context/project/architecture.md)               | Before structural changes, new modules, or protocol work |
| [Kurrent.Surge](.claude/context/project/client.md)                    | Before working on the client library                     |
| [Builder Conventions](.claude/context/project/builder-conventions.md) | Before adding builders, factories, or fluent APIs        |
| [Packaging](.claude/context/project/packaging.md)                     | Before adding project references to packable projects    |
| [Module Organization](.claude/context/project/module-organization.md) | Before adding files, models, or mappers to a module      |
| [Docs Guidelines](.claude/context/project/docs-guidelines.md)         | Before writing or updating documentation                 |

### XML documentation

XML docs follow the devx standard bundled with the `devx-xmldoc-audit` skill: docs state current behavior
only (never a changelog); public members get complete, terse docs with multiline summaries; `<remarks>` only
for hazards and contracts; internal members documented only when non-obvious; cross-references
actionable-only; `<example>` and `<seealso>` banned. Domain vocabulary lives in [CONTEXT.md](CONTEXT.md).

Repo addenda for the audit:

- Module client summaries read `Provides operations for [module] in KurrentDB.`
- Exemption: source-generator-emitted implicit conversion operators on `IVariant` types.

## Agent skills

### Issue tracker

Issues live in **Linear** — DevEx team, `Kurrent SDK .NET` project, referenced as `DEV-XXXX` and operated
through the Linear MCP. PRs are not a request surface.

Repo coordinates and label taxonomy live in `LINEAR.md` (repo root) — the contract read by the global
`devx-linear-workflow` skill (MUST be consulted before any Linear operation). Background on the current
rules is also written up in `.claude/context/project/issue-tracker-linear.md`.

### Triage labels

The five canonical triage roles map onto Linear's native workflow states plus the classification labels in
`devx-linear-workflow`; the role strings are not configured labels and issue status is never set manually.

### Domain docs

- Single-context: `CONTEXT.md` at the repo root.
- Existing reference docs under `.claude/context/project/`.
- ADRs are not part of our doc workflow, but some skills under evaluation emit them — if one is ever created
  it goes in `.claude/context/docs/adr/`, reserved so those skills work.

<!-- markdownlint-disable MD025 MD013 -->
<!-- jbcontext-instructions-start -->
# Tools

## Semantic Code Search (jbcontext)

You have access to `jbcontext search` for searching the codebase semantically.
Use the `/context-search` skill or run `jbcontext search "<query>"` to find code by meaning, not just keywords.

### Query Tips

- Be descriptive: "Where is a function that validates user email addresses" > "email"
- Include context: "Find error handling middleware for HTTP requests with logging"
- Specify what you're looking for: "React component that renders a modal dialog"

### When to use

`jbcontext search` is a **code-discovery** tool. Reach for it only when a task requires finding or understanding code whose location you don't already know.

Skip it — go straight to the right tool — when:
- the task names the exact file, class, or symbol (keyword grep is faster);
- the relevant file is already open or identified;
- the task doesn't involve locating code at all — git operations (rebase, merge, commit), running tests or builds, shell/statusline/config setup, or reviewing a diff you already have.

### How to use it
- Start with `jbcontext search` before planning, editing, or exact search in unfamiliar code when you do not yet know the right file, subsystem, implementation, or related test.
- Use one focused natural-language query per search.
- Do not start with grep, ripgrep, or find when the search problem is still semantic or exploratory.
- Inspect the first relevant file or directory before issuing another broad semantic search.
- Use another broad `jbcontext search` only if the local path stops being productive.
- Once you know the relevant file, symbol, or directory, switch to direct file reads or exact search for local inspection.
- If you search again after finding a relevant area, narrow with `-p <path>`.

<!-- jbcontext-instructions-end -->
<!-- markdownlint-restore -->