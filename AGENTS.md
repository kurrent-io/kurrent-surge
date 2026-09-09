# Kurrent .NET SDK

> .NET monorepo SDK for the Kurrent platform. It ships two independently installable packages: a gRPC
> client and a stream-processing runtime.

This file documents only what breaks convention. If a senior developer would guess it correctly from the
codebase, it does not belong here. Do not add standard patterns, paraphrased docs, or facts you can find in
a single config file. When you edit this file, hold that bar.

## Working rules

Each one cost a session.

- **Read the reference before you design.** When someone names a site, repo, or product as the thing to
  match, read its source config first. Clone it if you need to. Never reverse-engineer it from a
  screenshot. The answer is usually a dozen lines someone already wrote.
- **Check the skills list before the first edit in a domain**, not after you fail. Do the same for the
  framework's own docs and for its source in `node_modules`.
- **Solve the stated problem.** Do not invent requirements. Do not build a mechanism beside the framework
  when configuring the framework would do.
- **Verify a UI change in a browser** by measuring the rendered elements. A green build, a passing grep,
  or correct markup in `dist/` is not verification.
- **A probe that reports "missing" is unproven** until it shows which element it queried. Return a control
  value beside the value under test. Never change code on a negative result alone.
- **Name the environment once.** Confirm which URL and which server the reader is on before you report
  anything about it.
- **An earlier recommendation never outranks a current instruction.** If the two conflict, say so in one
  line, then follow the instruction.

## Stack

The SDK targets net10.0 only. Use the latest net10 APIs. Source generators are the exception. They target
netstandard2.0 because Roslyn requires it.

Never pass `--framework` to a solution-wide `dotnet build` or `dotnet test`. It forces one TFM onto the
whole solution and breaks the source-generator projects with `NETSDK1005`.

The Streams module uses C# 14 extension members (`extension(Type t)`).

There is no meta-package. Consumers install only what they need.

This is a greenfield project. There are no legacy compatibility constraints and no existing user base.

## Build and test

Aspire manages the test containers (`test/Aspire.Hosting.KurrentDB`). The test harness generates the TLS
certificates, so you do not set them up by hand.

When you consolidate several experimental versions into one implementation, keep the old files until the
new version builds and its tests pass.

When a failing test shows a mismatch between what the test expects and what the code produces, for example
the test expects snake_case and the code produces kebab-case, do not change either side on your own.
Report the mismatch, offer both options, and wait for direction. A heuristic such as "the majority of call
sites do it this way" is not permission to decide.

Before you refer to any project, file, or path by name, verify that it exists on disk. Do not assume a
sibling project exists because the naming convention suggests it.

Playground and throwaway projects (`playground/`, `.claude/context/.scratch/`) always target net10.0. They
are not consumer-facing, so the minimum-supported-TFM rule does not apply. `playground/` is tracked and
ships with the repository. Only `.claude/context/.scratch/` is throwaway.

To debug a CI or workflow problem, create a minimal throwaway workflow that targets the failing step
instead of running the full pipeline.

`tools/` holds .NET 10 file-based apps: `.cs` files with `#:` directives. There is no tool manifest and the
tools are not published to any feed, so run them from source:

```bash
dotnet run tools/semver-bump.cs -- --help
dotnet run tools/semver-bump.cs -- check   # needs GITHUB_TOKEN and GITHUB_REPOSITORY
```

CI is the exception. `.github/actions/install-tools/action.yml` packs every `.cs` file under `tools/` and
`scripts/` that declares `ToolCommandName`, installs it to `.artifacts/tools`, and appends that directory
to `PATH`. Only in CI does `semver-bump` resolve as a bare command.

Three scripts are the **only** supported way to do their job. Never hand-roll a substitute for one:

| Job | Script |
|---|---|
| Run tests | `scripts/testing/test-runner.cs`, never `dotnet test` |
| Run benchmarks | `scripts/benchmarks/run-benchmarks.sh` |
| Validate a package | `scripts/verify-package.py`, never unzip a `.nupkg` by hand |

## Protocols

The gRPC surface is split across two protocol versions. V2 handles appends, registry, indexes, and
connectors. V1 legacy handles reads and management. The V2 read path is commented out because those RPCs
do not exist in the proto yet. Proto definitions live in `proto/kurrentdb/protocol/{v0,v1,v2}/`.

## Structure

Two packages ship:

```text
Kurrent.Surge         gRPC client and processor runtime
Kurrent.Surge.Core    contracts, message catalogs, and the JSON, Protobuf, and Avro codecs
```

Regenerate that list with `grep -l "IsPackable>true" src/*/*.csproj`.

`Variant` and `Kurrent.CodeAnalysis` are solution-level libraries and are not published.
`Kurrent.Surge.Generators` and `Kurrent.Surge.Schema.Catalogs.Generators` are not packages either, but
their DLLs ship inside `Kurrent.Surge.Core` under `analyzers/dotnet/cs/`.

`Kurrent.Surge.Generators` emits three things: unique-identifier value types, Avro codecs, and options
validation.

`Kurrent.Surge/Client/Legacy/` holds V1 gRPC compatibility. It is a folder, not a package.

The Streams module is split across 13 partial class files. The naming convention is in
[Module Organization](.claude/context/project/module-organization.md).

Sum types use source-generated `IVariant` discriminated unions.

### Documentation site

`docs/` is the live documentation site, built with Astro and Starlight.
`.github/workflows/docs.yml` builds it on any push that touches `docs/**` and publishes it to GitHub
Pages.

### Examples

`examples/` holds 18 runnable projects, carried by `examples/Examples.slnx` and by `Kurrent.Surge.slnx`.
Read one before you hand-write a usage sample.

```text
examples/streams/     QuickStart (38 lines, the minimal complete program), InventoryManagement
examples/processors/  WordCount, FraudDetection, IoTMonitoring, Leaderboard, OrderEnrichment,
                      ClickstreamSessions, WithPostgres, WithRedisCache, FullExample,
                      AspNetMinimal, Connector.Management
examples/registry/    Quickstart, OrderManagement
examples/             PackageTest, PackageTest.Streaming, Transfermarkt.Domain
```

No example covers indexes, the Avro codec, or the admin API.

`examples/README.md` is not an index. It documents message-catalog naming and names three projects as
illustrations. Every example carries its own README except the two `PackageTest` projects and
`Transfermarkt.Domain`. Those three are a package-resolution smoke test and a shared domain library, not
usage samples.

CI builds the examples twice. The first pass runs inside the root solution. The second runs
`dotnet build examples/Examples.slnx -c Release -p:TestPackages=true`, which flips the `PackageTest`
projects from a `ProjectReference` to a real `PackageReference` against the locally packed feed. A failure
in the second pass means the published package is broken, not the example.

## Project context

The knowledge base lives at `.claude/context/`. Its structure, frontmatter shapes, and rules live in the
`devx-knowledge-base` skill. Plan-mode scratch goes in `.claude/plans/`, throwaway files in
`.claude/context/.scratch/`.

## Reference

Read these before you work on the related area. Do not read them all up front.

| Document | When to read |
|---|---|
| [Architecture](.claude/context/project/architecture.md) | Before structural changes, new modules, or protocol work |
| [Kurrent.Surge client](.claude/context/project/client.md) | Before working on the client library |
| [Packaging](.claude/context/project/packaging.md) | Before adding project references to packable projects |
| [Module Organization](.claude/context/project/module-organization.md) | Before adding files, models, or mappers to a module |
| [Docs Guidelines](.claude/context/project/docs-guidelines.md) | Before writing or updating documentation |

### XML documentation

XML docs follow the devx standard bundled with the `devx-xmldoc-audit` skill. Docs state current behavior
and never read as a changelog. Public members get complete, terse docs with multiline summaries. Use
`<remarks>` only for hazards and contracts. Document internal members only when they are non-obvious. Keep
cross-references actionable. `<example>` and `<seealso>` are banned. Domain vocabulary lives in
[CONTEXT.md](CONTEXT.md).

Two repo addenda for the audit:

- A module client summary reads `Provides operations for [module] in KurrentDB.`
- Implicit conversion operators emitted by a source generator on `IVariant` types are exempt.

## Agent skills

### Issue tracker

Issues live in Linear, on the DevEx team, in the `Kurrent SDK .NET` project. They are referenced as
`DEV-XXXX` and operated through the Linear MCP. Pull requests are not a request surface.

`LINEAR.md` at the repo root holds the repo coordinates and the label taxonomy. It is the contract that
the global `devx-linear-workflow` skill reads, and you MUST consult it before any Linear operation. The
generic process and the fixed DevEx facts live in that skill, not in this repo.

### Triage labels

The five canonical triage roles map onto Linear's native workflow states plus the classification labels in
`devx-linear-workflow`. The role strings are not configured labels. Never set issue status by hand.

### Domain docs

- Single-context: `CONTEXT.md` at the repo root.
- Reference docs: `.claude/context/project/`.
- ADRs are not part of this doc workflow, but some skills under evaluation emit them. If one is ever
  created it goes in `.claude/context/docs/adr/`, which is reserved so those skills work.

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