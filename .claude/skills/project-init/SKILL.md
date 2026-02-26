---
name: project-init
description: Generate or update .project/ documentation structure. Use when initializing a new
  project, onboarding, or when project context files are missing, empty or outdated.
argument-hint: [force]
---

Analyze this codebase and generate (or update) the `.project/` documentation structure. This is the project's
persistent context for coding agents and developers.

## Contents

- [Arguments](#arguments)
- [Philosophy](#philosophy)
- [Editorial standard](#editorial-standard)
- [Phase 1: Discovery](#phase-1-discovery)
- [Phase 2: User review](#phase-2-user-review)
- [Phase 3: Generation](#phase-3-generation)
- [Phase 4: Completion](#phase-4-completion)

## Arguments

- No argument: update existing docs if `.project/project.md` exists, otherwise generate from scratch
- `force`: discard existing `.project/` files and generate everything from scratch

## Philosophy

Document **only what breaks convention**. If a senior developer would guess it correctly from the codebase, omit
it. Custom patterns, unorthodox tooling, surprising constraints, multi-step procedures, and anything that would
save time for someone joining deserve documentation.

This principle governs every content decision. When in doubt, leave it out.

## Editorial standard

These rules constrain all phases. Read before discovery; apply during generation.

### Include

- Anything that would surprise a senior developer examining this codebase
- Cross-cutting patterns not obvious from any single file
- Non-obvious build, test, deployment, or setup workflows
- Security-relevant patterns (auth flows, secrets management, threat model)
- References to where rules or conventions live; do not copy their content
- `file:line` pointers to authoritative code, not pasted snippets

### Exclude

- File trees, directory listings, dependency lists, API surface enumerations
- Generic practices ("write tests", "handle errors", "use meaningful names")
- Facts discoverable from a single config file (`.csproj`, `global.json`, `Directory.Build.props`)
- Code style rules enforced by `.editorconfig`, linters, or formatters
- Standard project structure narration ("src/ contains source code")
- Explanations of standard technologies; the agent already knows them
- Paraphrased external documentation; point to official sources instead
- Speculative "tips" sections not grounded in project files
- Information already in `README.md`
- Timestamps and version numbers unless they determine code behavior

### Format

- Each fact lives in one place. Do not repeat information across files.
- Follow the structure in the corresponding template (see [Templates](#templates)).

## Phase 1: Discovery

### Mode detection

If the `force` argument was passed, enter fresh mode regardless of existing files.

Otherwise, check whether `.project/project.md` exists.

- **Exists**: enter review mode. Read all existing `.project/` files first, then scan the codebase for deltas:
  what is outdated, missing, or violates the philosophy.
- **Does not exist**: enter fresh mode. Full codebase scan.

### Parallel agents

Dispatch three read-only agents in parallel using the `Task` tool (`subagent_type: "Explore"`) to scan independent
categories. You (the lead) act as strategist and synthesize their findings.

**Compliance agent**: Scan agent instruction files (CLAUDE.md, AGENTS.md, GEMINI.md,
`.github/copilot-instructions.md`, `.junie/guidelines.md`), validate policy consistency, check for secrets or
sensitive config in tracked files.

**Build agent**: Analyze build pipelines, tooling, CI/CD patterns, test infrastructure. Identify non-standard
workflows, custom build steps, unusual tooling choices.

**Structure agent**: Examine codebase layout, entry points, module boundaries, protocol layers. Identify monorepo
structures, multi-workspace patterns, or unusual architectural boundaries.

Each agent prompt must include:

- The agent's specific focus area (from above)
- The editorial standard (include/exclude lists from this skill)
- Output format: key findings, risks identified, `file:line` pointers
- In review mode: the existing `.project/` file content relevant to their area

### Synthesis

After all agents report back:

- Consolidate findings across all three agents
- Flag monorepos, multi-workspace projects, or unusual structures that affect the documentation approach
- Identify security implications (secrets in config, credential patterns)
- Detect documentation gaps
- Determine which topics warrant their own `project-{topic}.md` file. The bar: if working in that area without
  reading the reference file would lead to mistakes or wasted time, it deserves its own file.
- In review mode: identify which existing files need updates, additions, or removals

## Phase 2: User review

Present findings and gather approvals in a single consolidated interaction. Use `AskUserQuestion` with concrete
options. Minimize the number of sequential prompts.

### Fresh mode

One `AskUserQuestion` call with up to 3 questions:

**Q1 (required)**: Topic approval.

- header: `Topics` (max 12 chars)
- multiSelect: true
- List each discovered topic as an option with a description explaining why it warrants its own file
- If more than 4 topics: batch across multiple questions or group into categories
- The automatic "Other" option lets the user suggest additional topics

**Q2 (conditional)**: Symlink configuration. Only ask if changes are needed (see [Symlinks](#symlinks) for
detection logic). Skip entirely if all paths are already correct.

- header: `Symlinks`
- multiSelect: true
- List only paths that need action, with descriptions noting the consequence

**Q3 (conditional)**: Ambiguity resolution. Only if the agents found conflicting patterns or the codebase has
structural ambiguity (monorepo scope, competing conventions).

- header: `Scope` or `Conflict`
- Single-select with 2-3 options describing the competing approaches

### Review mode

One `AskUserQuestion` call with up to 3 questions:

**Q1**: Proposed changes approval.

- header: `Changes`
- multiSelect: true
- Group changes by action: updates to existing content, new sections, removals
- Use `markdown` preview on options to show before/after comparisons when helpful

**Q2 (conditional)**: New topics. Only if the review discovered areas that lack coverage.

- header: `New Topics`
- multiSelect: true

**Q3 (conditional)**: Symlink changes. Same logic as fresh mode.

### Fallback behaviors

- **User rejects all topics**: generate `project.md` only. Note in the final report that no topic files were
  created.
- **Discovery finds nothing non-standard**: report this to the user. Confirm whether to generate a minimal
  `project.md` or skip entirely.
- **Agent returns no findings**: proceed with available data. Note the gap in synthesis.

## Phase 3: Generation

### Templates

Read templates from `templates/` (relative to this skill file) before generating files.

| Template           | Use when                  | Output path                   |
|--------------------|---------------------------|-------------------------------|
| `project.md`       | Always; the lobby file    | `.project/project.md`         |
| `project-topic.md` | Each approved topic       | `.project/project-{topic}.md` |
| `init-report.md`   | Fresh mode; final summary | Displayed to user, not saved  |
| `review-report.md` | Review mode; changes      | Displayed to user, not saved  |

Omit any template section that would be empty. Never create files without meaningful content.

### File operations

- **Fresh mode**: use `Write` to create new files from templates.
- **Review mode**: use `Edit` for targeted changes to existing files. Do not rewrite entire files; preserve
  inline comments, formatting nuances, and content you did not change.

### Symlinks

`.project/project.md` is the single source of truth. After generating files, check
these paths:

- `CLAUDE.md`, `AGENTS.md`, `GEMINI.md` -> `.project/project.md`
- `.github/copilot-instructions.md`, `.junie/guidelines.md` -> `../.project/project.md`

For each path, determine status:

- **Already a symlink pointing correctly**: no action needed
- **Regular file exists**: user decision to replace (warn about data loss)
- **Does not exist**: user decision to create
- **Parent directory missing**: create directory first if user approves symlink

If all paths are already correct, skip the symlink question in Phase 2 entirely.

### Final report

Present the appropriate report template to the user:

- Fresh mode: use `init-report.md`
- Review mode: use `review-report.md`

## Phase 4: Completion

Ask the user one final question via `AskUserQuestion`:

- header: `Review`
- question: "Documentation generated. Want to adjust anything?"
- Options: "Looks good" / "Make changes" (with description: "Describe what to adjust using the Other option or
  select this to review specific files")

If the user requests changes, make the edits and present a final diff. Do not re-enter the full workflow.
