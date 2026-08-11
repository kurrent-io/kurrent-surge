<!-- devx-linear-workflow-contract: v1 -->
# Linear Workflow

Coordinates and conventions for tracking `kurrent-sdk` work in Linear. Consumed by the `devx-linear-workflow`
skill and by anyone triaging or filing issues by hand. Generic process (issue-first workflow, worktree-per-
issue, estimation scale, ATDD/EARS, sitrep) and fixed DevEx facts (team, team id, issue-id shape, sprint
capacity — the same across every DevEx repo, since Linear scopes cycles to the team, not the project) live
in the skill itself. This file covers only what's specific to this repo: the Linear project, label taxonomy,
milestone naming, PR/CI specifics, and definition-of-done doc paths.

## Coordinates

| Field   | Value             |
|---------|--------------------|
| Project | Kurrent SDK .NET   |

All SDK work across languages lives under the single **Kurrent SDK** initiative, with one long-lived Linear
project per language (`Kurrent SDK .NET`, `Kurrent SDK Node`, `Kurrent SDK Java`, `Kurrent SDK Go`, `Kurrent
SDK Python`, `Kurrent SDK Rust`). This repo is **Kurrent SDK .NET**. New language projects are created when
work starts on that language — not before.

## Labels

Lowercase kebab-case, organized in mutually exclusive groups. Apply in order: Platform → SDK package →
Projectors (if any) → General (if any).

### Platform

Language/runtime. Always apply, for cross-project visibility.

`dotnet` · `nodejs` · `java` · `go` · `python` · `rust`

### SDK package

| Label                 | When to apply                                                                      |
|-----------------------|---------------------------------------------------------------------------------------|
| `sdk-client`          | Kurrent.Client — streams, schema registry client, admin, management                  |
| `sdk-streaming`       | Kurrent.Streaming — processors, projectors core, checkpointing, stream processing    |
| `sdk-schema-catalogs` | Kurrent.Schema.Catalogs — catalogs, attributes, naming, coders, catalog generator     |

### Projectors

Only alongside `sdk-streaming`.

| Label    | When to apply          |
|----------|--------------------------|
| `duckdb` | DuckDB projector work   |

### General

`Bug` · `Feature` · `Improvement` · `Test` · `legacy`

### Recognizing new components

When a new projector target, connector, or SDK package shows up, suggest a new label in the right group
(create a new group on first use, e.g. `connectors`), wait for approval, create it scoped to the DevEx team,
then add it to the tables above.

## Milestones

Milestones track releases. Name them `{platform}-{package}-v{version}` so they read clearly at the
initiative level, where the project context isn't visible:

```text
dotnet-client-v2.0.0-preview.1
dotnet-client-v2.0.0
dotnet-streaming-v1.0.0-preview.1
```

## Pull requests

PR title must be a valid conventional commit; CI enforces it via `amannn/action-semantic-pull-request`. Exact
scopes and commit types are owned by the `devx-git-conventions` skill, not repeated here.

## Definition of done

- `docs/` updated if the change affects user-facing behavior.
- `examples/` updated if the change introduces new API surface or patterns.

## Scope fences

| Concern | Owned by |
|---|---|
| Git commit/PR title format, scopes, past-tense rules | `devx-git-conventions` skill |

