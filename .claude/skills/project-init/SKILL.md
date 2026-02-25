---
name: project-init
description: >
  Generate or update .project/ documentation structure.
  Use when initializing a new project, onboarding, or when
  project context files are missing or outdated.
argument-hint: [update]
---

Analyze this codebase and generate (or update) the `.project/` documentation structure. This is the project's
persistent context for coding agents and developers.

## Existing context

Before generating, scan for and read these if they exist. Use them to inform your understanding. Do not duplicate their
content; reference where relevant.

Agent instruction and rule files:
- `CLAUDE.md`, `CLAUDE.local.md`, `.claude/rules/`
- `AGENTS.md`, `AGENT.md`, `AGENTS.override.md`
- `GEMINI.md`
- `.github/copilot-instructions.md`
- `.github/instructions/**/*.instructions.md`
- `.cursor/rules/`, `.cursorrules`
- `.windsurfrules`
- `.clinerules`
- `.continuerules`, `.continue/rules/`
- `.junie/guidelines.md`
- `.aiassistant/rules/`

Project documentation:
- `README.md`, `CONTRIBUTING.md`, `CONVENTIONS.md`
- `PROJECT.md`, `ARCHITECTURE.md`
- `docs/` or `doc/` directory (scan for architecture or onboarding docs)

Build and tooling config:
- `.editorconfig` (to know what NOT to document in code conventions)
- CI/CD configs (`.github/workflows/`, `.gitlab-ci.yml`, `Jenkinsfile`, `Makefile`, `Taskfile.yml`, `justfile`)
- Dependency manifests and lock files
- Solution/project/workspace files
- `global.json`, `Directory.Build.props`, `package.json`, `pyproject.toml`, `Cargo.toml`, `go.mod`, `build.gradle`,
  `pom.xml`, or equivalent

## Rules

### User interaction

Whenever this skill requires user input (approvals, choices between options, clarifications), use the `AskUserQuestion`
tool. Do not present questions as plain text and hope the user responds. Explicitly invoke the tool so the interaction
is structured and the skill blocks until a response is received.

### Content

- Include only information that requires reading multiple files to understand
- Include cross-cutting patterns not obvious from any single file
- Include anything that would surprise a senior developer new to the project
- Include build/test commands with non-obvious flags or ordering
- Reference where rules or conventions live (e.g., "see `.claude/rules/` for X") when relevant, but do not copy their
  content

### Exclusions (the agent will figure these out)

- File trees and directory listings
- Dependency lists
- API surface per file
- Generic practices ("write tests", "handle errors", "use meaningful names")
- Code style rules enforced by `.editorconfig`, linters, or formatters
- Speculative "tips" sections not grounded in actual project files
- Information already in `README.md` that the agent can read on demand
- Timestamps, dates, and version numbers unless core to a topic (e.g., a protocol version that determines which code
  path is used)

### Format

- Use `file:line` pointers to authoritative code, not pasted snippets
- Do not repeat information across files. Each fact lives in one place
- Every line must earn its place. If removing it would not hurt an agent working in this codebase, remove it

### Topic discovery

After exploring the codebase, create a `project-{topic}.md` for any area that has specialized instructions, workflows,
or patterns an agent would not guess from the code alone. Examples:

- Benchmarking: benchmark harness, how to run and compare results, baseline expectations
- Security: authentication flows, secrets management, threat model notes
- Deployment: release process, environment-specific configuration
- Protocols: non-obvious wire formats, version negotiation, migration paths

The bar: if working in that area without reading the reference file would lead to mistakes or wasted time, it deserves
its own file.

### Reference file rules

All reference files (everything except `project.md`) share these rules:
- Target: under 100 lines. Shorter is better
- If a file exceeds 100 lines, add a `## Contents` section with anchor links immediately after the title
- Omit any section that would be empty
- Do not create a file if it would have no meaningful content

### When files already exist

When `.project/` files already exist, enter review/update mode:
- Read all existing files first to understand what is already documented
- Review each file against this skill's own rules (Content, Exclusions, Format, Reference file rules). Flag violations
  as Update or Remove items in the report, just like stale or missing content
- Do NOT write or edit any file. Instead, present a review report using the template in
  [templates/review-report.md](templates/review-report.md)
- Repeat the `## {filename}` section for each file that has changes
- Only include sections (Update, Add, Remove, New files) that have items
- Use `AskUserQuestion` to ask the user to approve, reject, or adjust each proposed change. Do not proceed until you
  receive a response
- Apply only approved changes. Use `AskUserQuestion` again if you need clarification or want to iterate on rejected
  items
- The end result must meet the same quality bar as a fresh generation: compact, accurate, progressively disclosed,
  no bloat

### Agent instruction symlinks

[.project/instructions.md](../../.project/instructions.md) is the single source of truth for agent instructions. After
generating project files, check these paths and report their status to the user:

Symlink targets (relative to repo root unless noted):
- `CLAUDE.md` → `.project/instructions.md`
- `AGENTS.md` → `.project/instructions.md`
- `GEMINI.md` → `.project/instructions.md`
- `.github/copilot-instructions.md` → `../.project/instructions.md`
- `.junie/guidelines.md` → `../.project/instructions.md`

For each path, one of three things is true:
1. **Already a symlink** pointing to `instructions.md` → no action needed
2. **Exists as a regular file** → needs user decision (replace with symlink?)
3. **Does not exist** → needs user decision (create as symlink?)

Present all findings in a simple table, then use `AskUserQuestion` to ask which symlinks the user wants to create or
replace. For regular files that would be replaced, warn that the original content will be lost unless they review it
first. Create parent directories (e.g., `.junie/`, `.github/`) if needed. Never create or replace symlinks without
explicit user approval.

## Output

### Fresh generation (no existing `.project/` files)

Write files directly using their respective templates:
1. `project.md` using [templates/project.md](templates/project.md)
2. `project-architecture.md` using [templates/project-architecture.md](templates/project-architecture.md)
3. `project-code-conventions.md` using [templates/project-code-conventions.md](templates/project-code-conventions.md)
   (only if there are conventions tooling cannot enforce)
4. `project-testing.md` using [templates/project-testing.md](templates/project-testing.md) (if the project has tests
   with non-obvious runner commands, fixture patterns, categories, or integration infrastructure)
5. One `project-{topic}.md` per discovered topic using [templates/project-topic.md](templates/project-topic.md)

Add one row to the Reference table in `project.md` for each generated reference file. A topic can be a package, module,
subsystem, domain concept, or any area important enough to need more detail than the lobby provides.

After writing all files, present a summary using the template in [templates/init-report.md](templates/init-report.md).
Do not show file contents in the summary. Then use `AskUserQuestion` to ask if the user wants to adjust any of the
generated files.

### Review/update (existing `.project/` files)

Do NOT write or edit any file. Present a review report using the template in
[templates/review-report.md](templates/review-report.md), then use `AskUserQuestion` to ask the user which items to
approve, reject, or adjust. See "When files already exist" above for the full workflow.
