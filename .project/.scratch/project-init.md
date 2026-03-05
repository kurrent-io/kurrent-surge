Analyze this codebase and generate (or update) the `.project/` documentation
structure. This is the project's persistent context for coding agents and
developers.

## What to generate

### 1. `project.md` (the lobby)

The main file. Under 40 lines. Contains only:

- **Identity**: project name and one-sentence description
- **Stack**: language version, framework, runtime, solution format, package
  manager, key infrastructure. Only what is non-obvious
- **Build and Test**: exact shell commands for build, test all, test single.
  Include non-obvious setup steps (containers, env vars, certificates).
  Omit if standard and discoverable from CI config
- **Reference table**: a table linking to detail files with a "when to read"
  column. This IS the progressive disclosure mechanism

Reference table format:

```markdown
## Reference

Read these before working on related areas. Do not read all of them upfront.

| Document | When to read |
|----------|-------------|
| [Architecture](project-architecture.md) | Before structural changes or new modules |
| [Code Conventions](project-code-conventions.md) | Before writing new code in unfamiliar areas |
| [Decisions](project-decisions.md) | Before proposing alternatives to existing patterns |
```

Add one row per discovered package/module that warrants its own detail file.
Only create detail files for active packages, not planned or deprecated ones.

### 2. `project-architecture.md`

High-level structure only. Under 100 lines. Contains:

- Module/package map with status and relationships
- Layer boundaries and dependency direction
- Entry points and how they connect
- Transport or protocol details that span multiple files
- Use `file:line` pointers to authoritative code, not pasted snippets

### 3. `project-code-conventions.md`

Only conventions that cannot be enforced by `.editorconfig`, linters, or
formatters. Under 40 lines. Examples of what belongs:

- Domain-specific naming patterns (e.g., "streams use X, registries use Y")
- Error handling approach (Result types vs exceptions, and where each applies)
- Patterns that are project-specific and not industry-standard

Do NOT include: brace style, indentation, import ordering, line length, or
anything a linter handles.

### 4. `project-decisions.md`

Active architectural decisions that would surprise someone or contradict
common defaults. Under 60 lines. ADR-lite format per entry:

```markdown
### {Short title}
{Decision}. {Rationale in one sentence.}
```

Only include decisions that are still active and affect how an agent should
work in the codebase. Remove stale decisions.

### 5. `project-{package}.md` (one per active package/module)

Package-specific detail. Under 100 lines each. Contains:

- Module architecture and operations
- Key types and their relationships
- Status per sub-module (active, in-progress, commented out)
- Transport or protocol specifics for that package

## Rules

### What to include

- Information that requires reading multiple files to understand
- Cross-cutting patterns not obvious from any single file
- Anything that would surprise a senior developer new to the project
- Build/test commands with non-obvious flags or ordering

### What to exclude (the agent can figure these out)

- File trees and directory listings (agent can `ls` and `glob`)
- Dependency lists (agent can read manifest files)
- API surface per file (agent can read the file)
- Generic development practices ("write tests", "handle errors")
- Code style rules enforced by `.editorconfig` or linters
- Obvious information that any developer would know
- Speculative or "tips" sections not grounded in actual project files

### Format rules

- Use `file:line` pointers, not pasted code. Pasted code rots
- Do not repeat information across files. Each fact lives in one place
- Do not fabricate sections like "Common Tasks", "Tips", or "Support"
  unless the content is expressly found in project files
- Every line must earn its place. If removing it would not hurt an agent
  working in this codebase, remove it

### Existing context to scan

Before generating, read these if they exist and incorporate relevant parts:

- `README.md`
- `.cursor/rules/` or `.cursorrules`
- `.github/copilot-instructions.md`
- `AGENTS.md` or `CLAUDE.md` (if not a symlink to instructions.md)
- `.editorconfig` (to know what NOT to document in code conventions)
- CI/CD configs (`.github/workflows/`, `Makefile`, etc.)
- Solution/project files, `Directory.Build.props`, `global.json`

### When updating existing files

- If `.project/` files already exist, update them. Do not recreate from scratch
- Preserve manually written content. Only modify sections that are outdated
  or incomplete based on current codebase state
- Flag any content that appears stale but do not delete it without confirmation
- Add new packages/modules discovered since the last run
- Remove references to packages/modules that no longer exist
