<!-- devx-code-review-contract: v1 -->
# Code Review Standards

Standards source for reviewing changes in this repo. Consumed by review agents (the `devx-code-review` skill's
two axes and any sub-agent reviewing a diff) and by human reviewers. This file is an index plus the
review-only rules written nowhere else — the standards themselves live in the documents below. Do not
duplicate their content here; when a rule and its source doc disagree, the source doc wins.

## Where the standards live

Read the two convention files for every review. Read the area docs only when the diff touches that area.

| Concern | Source | Read when |
|---|---|---|
| C# code conventions (style, async, NRT, AOT bans, sealing, collections, extension members) | [.agents/system-prompts/400-dotnet-conventions.md](.agents/system-prompts/400-dotnet-conventions.md) | Always |
| Testing conventions (TUnit, runner, naming, assertions, Bogus, AAA) | [.agents/system-prompts/500-dotnet-testing.md](.agents/system-prompts/500-dotnet-testing.md) | Always |
| Repo-level rules (stack, TFM policy, protocol split, package structure) | [CLAUDE.md](CLAUDE.md) | Always |
| Domain vocabulary | [CONTEXT.md](CONTEXT.md) | Naming new public concepts |
| Module and file organization | [.claude/context/project/module-organization.md](.claude/context/project/module-organization.md) | New files, models, mappers |
| Builders, factories, fluent APIs | [.claude/context/project/builder-conventions.md](.claude/context/project/builder-conventions.md) | Builder or options changes |
| XML documentation | [.claude/context/project/xmldoc.md](.claude/context/project/xmldoc.md) | Public API doc changes |
| Packaging and project references | [.claude/context/project/packaging.md](.claude/context/project/packaging.md) | csproj / reference changes |
| Architecture boundaries | [.claude/context/project/architecture.md](.claude/context/project/architecture.md) | Structural or cross-module changes |
| Client library specifics | [.claude/context/project/client.md](.claude/context/project/client.md) | `Kurrent.Client` changes |
| Documentation writing | [.claude/context/project/docs-guidelines.md](.claude/context/project/docs-guidelines.md) | Docs changes |

## Skip what tooling enforces

Do not report findings the pipeline already catches:

- Formatting, whitespace, brace style, and naming style covered by [.editorconfig](.editorconfig).
- Anything the build or an analyzer fails on — it cannot reach review broken.

Review effort belongs on what tooling cannot see: design, conventions, correctness, intent.

## Review-only rules

Rules that govern the review itself and are documented nowhere else:

1. **Greenfield API surface.** This SDK is pre-release with no users. Public member removals, renames, and
   signature changes are normal deltas — report them as changes, never as breaking-change risks or blockers.
2. **Zero callers is not deletion authorization.** Report apparently unused code as a finding; never state
   "safe to delete" as fact or propose removal as the fix.
3. **Findings are report-only.** Fixes — including trivial or stylistic ones — require explicit approval
   before being applied. Present options; do not act.
4. **Label the basis.** Each finding is either a *hard violation* (cite the source file and the rule) or a
   *judgment call* (say so). Never present a judgment call with a violation's confidence.
5. **No tracker IDs in code.** `DEV-XXXX` references in comments are a finding. Comments must be
   self-contained in domain terms; issue IDs belong in commits, PRs, and Linear.
6. **Comments explain WHY.** Flag comments that narrate clear code, and non-obvious code (concurrency,
   lifecycle, workarounds, protocol quirks) that carries no rationale.
7. **Tests default to integration.** Prefer coverage against the real fixture/server. Flag production seams
   introduced solely to enable fakes or mocks, and unit tests standing in where an integration test is the
   honest check.

## Spec sources

Discovery order for the originating spec:

1. `DEV-XXXX` issue IDs in the branch name or commit messages — fetch via the Linear MCP; consult the
   `devx-linear-workflow` skill before any Linear operation.
2. Knowledge-base feature folders — `.claude/context/docs/features/<feature>/spec/spec.md`, falling back to
   `prd/prd.md`, matched by branch name or feature slug.
3. An explicit path passed by the user.
4. Nothing found → ask. If there is no spec, the Spec axis skips and reports "no spec available".

## Report destination

File the full report at `.claude/context/docs/reports/YYYY-MM-DD-HHMM-<slug>/report.md` with report
frontmatter (`type: review`), then summarize in conversation citing the path.

## Scope fences

| Finding type | Owned by |
|---|---|
| Test-code style and conventions (assertions, Bogus, AAA, TUnit usage) | `devx-test-review` skill |
| Correctness bug hunting on the diff | `/code-review` |
| Security review of pending changes | `/security-review` |

Name fenced findings as one-line pointers to the owner; do not develop them into full findings.

## Finding format

Every finding states: location (`file:line`), nature (what rule or judgment it violates, with the source
cited per rule 4), and severity (LOW / MODERATE / HIGH / CRITICAL).
