<!-- devx-xmldoc-audit-contract: v1 -->
# XML Documentation Audit Contract

Repo bindings for the `devx-xmldoc-audit` skill. The conventions themselves live in the spec below — do not
duplicate them here; when this file and the spec disagree, the spec wins.

## Spec

The authority for every rule: [.claude/context/project/xmldoc.md](.claude/context/project/xmldoc.md).

## Audit targets

| Setting          | Value                                                                                    |
|------------------|-------------------------------------------------------------------------------------------|
| Default targets  | `src/Kurrent.Surge`, `src/Kurrent.Surge.Core`                                             |
| Opt-in add-ons   | `Kurrent.Surge.Cli`, `.Mcp`, `.MessagePack`, `.MessagePack.Nerdbank`, `.Avro`, `.Schema.Catalogs` |
| Verify packables | `grep -l "IsPackable>true" src/*/*.csproj`                                                |
| Never sweep      | `src/Kurrent.Client/`, `src/Kurrent.Streaming/` — on disk with no `.csproj`, untracked leftovers |

## Repo-specific exemptions

- Source-generator projects (`netstandard2.0`).
- Source-generator-emitted implicit conversion operators on `IVariant` types.

## Module grouping

Group finder worklists by top-level module folder — e.g. `Client`, `Streaming`, `Configuration`,
`Processors`, `Projectors`, `Errors`, `Internal`.

## Report destination

`.claude/context/docs/reports/YYYY-MM-DD-HHMM-xmldoc-drift/report.md` with report frontmatter
(`type: drift`), then summarize in conversation citing the path.
