# Git Conventional Commits

Expert guidance for creating commit messages that follow the Conventional Commits v1.0.0 specification.

## Contents

- [Commit Message Structure](#commit-message-structure)
- [Primary Commit Types](#primary-commit-types)
- [Common Additional Types](#common-additional-types)
- [Breaking Changes](#breaking-changes)
- [Scope](#scope)
- [Writing Effective Descriptions](#writing-effective-descriptions)
- [When to Use Body](#when-to-use-body)
- [Multi-Concern Commits](#multi-concern-commits)
- [Semantic Versioning Relationships](#semantic-versioning-relationships)
- [Common Pitfalls to Avoid](#common-pitfalls-to-avoid)

## Commit Message Structure

```
<type>[optional scope]: <description>

[optional body]
```

## Primary Commit Types

**feat:** Introduces a new feature (correlates with MINOR in SemVer)
**fix:** Patches a bug (correlates with PATCH in SemVer)

## Common Additional Types

While not mandated by the specification, these types are widely adopted:

- **chore:** Routine tasks, maintenance (no production code change)
- **ci:** CI/CD, build system, project files, infrastructure, Docker, deployment
- **deps:** Dependency updates (version bumps, added/removed packages)
- **docs:** Documentation changes only
- **perf:** Performance improvements
- **refactor:** Code changes that neither fix bugs nor add features
- **revert:** Reverts a previous commit
- **security:** Vulnerability patches, dependency CVEs, auth hardening
- **style:** Code style changes (formatting, missing semicolons, etc.)
- **test:** Adding or modifying tests

## Breaking Changes

Append `!` after the type or scope to indicate a breaking change:

- `feat!:` or `feat(api)!:` or `fix!:` or `security!:`

Breaking changes correlate with MAJOR version bumps in SemVer. Always mark them clearly.

## Scope

Optional, lowercase, in parentheses after the type. A scope names a **logical area of the SDK, not a
package** — the same area spans both the `Kurrent.Surge.Core` contracts and the `Kurrent.Surge`
implementation. Use dot notation for module-level precision: `type(area.module):`.

### Areas

| Scope       | Logical area (spans contracts + implementation)                                               |
|-------------|-----------------------------------------------------------------------------------------------|
| `client`    | gRPC client surface — streams, registry, connectors, indexes, admin, legacy transport         |
| `streaming` | stream processing — processors, drones, data sources, checkpointing, partitioning, projectors |
| `schema`    | message catalogs, naming, resolution, and serialization                                       |
| `core`      | shared foundation used by both client and streaming — records, positions, errors, base options, diagnostics |
| `variant`   | Variant discriminated-union library and its source generator                                  |
| `testing`   | shared test infrastructure — testkit, Aspire host, datasets                                   |

### Module sub-scopes

Use `area.module` when a change is confined to one module within an area.

| Scope                     | Area                                       |
|---------------------------|--------------------------------------------|
| `client.streams`          | Stream read, write, and manage operations  |
| `client.registry`         | Schema registry                            |
| `client.connectors`       | Connector management                       |
| `client.indexes`          | Secondary indexes                          |
| `client.admin`            | Server administration (scavenge, etc.)     |
| `client.legacy`           | V1 legacy gRPC compatibility layer         |
| `client.config`           | Client configuration and options           |
| `streaming.processors`    | Processor/drone runtime and data sources   |
| `streaming.projectors`    | Projections and projection stores          |
| `streaming.checkpointing` | Checkpoint stores and position tracking    |
| `streaming.partitioning`  | Partition assignment and distribution      |

### Examples

```
feat(client.streams): added bidirectional read support
fix(streaming.checkpointing): corrected offset drift on rebalance
refactor(core): moved shared options onto the contract layer
perf(schema): reduced allocations in the message resolver
refactor(client.legacy): extracted shared retry logic
test(testing): added empty-stream read test
docs(client.registry): documented schema evolution rules
deps(variant): bumped source generator dependencies
```

## Writing Effective Descriptions

- Use past tense: "added", "fixed", "updated" (not "add", "fix", "update")
- Start with lowercase
- No period at the end
- Be specific but concise
- Limit to 50–72 characters when possible

## When to Use Body

When in doubt, include a body. Prefer more context over less.

Always use bullet points (`-`) in the body for readability.

A body is especially useful when:

- The description alone doesn't explain why the change was made
- Multiple related changes were made
- Implementation details help future understanding

## Multi-Concern Commits

When a single commit includes multiple types of changes (features, fixes, chores), use the most
significant change as the type and list the rest in the body:

```
feat: added user dashboard with profile settings

- fixed sidebar navigation overlap
- updated dependency versions
- refactored auth middleware for reuse
```

The type in the header reflects the primary change. Each additional concern gets a bullet in the
body with a lowercase prefix indicating its nature (fixed, updated, refactored, removed, etc.).

## Semantic Versioning Relationships

- **MAJOR (X.0.0):** Commits with BREAKING CHANGE (any type) or type with `!`
- **MINOR (0.X.0):** Commits with type `feat`
- **PATCH (0.0.X):** Commits with type `fix` or `perf` or `refactor` that doesn't introduce a new feature
- **No version change:** Other types (docs, chore, etc.)

## Issue tracker references

Never put Linear issue IDs in the commit subject line — the subject must remain a clean conventional commit
for `semver-bump` to parse correctly. Reference issues in the commit body if needed:

```
feat(client.streams): added bidirectional read support

- Closes DEV-1046
```

The PR title (which becomes the squash commit on main) follows the same rule: conventional commit only,
no issue IDs. Linear links PRs to issues automatically via branch names.

## Common Pitfalls to Avoid

1. **Mixing changes:** Prefer atomic commits, but when unavoidable use the multi-concern format above
2. **Vague descriptions:** "fix stuff" or "update code" are not helpful
3. **Present tense:** "add feature" should be "added feature"
4. **Missing type:** All commits must have a type prefix
5. **Inconsistent scopes:** Use established scope names
6. **Forgetting breaking changes:** Always mark API-breaking changes
7. **Excessive scope:** Commits should be atomic and focused
8. **Linear ID in subject:** Never prefix with `[DEV-XXXX]` — breaks CI validation and version detection
