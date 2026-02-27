## PROJECT CONTEXT

- Read [.project/project.md](.project/project.md) before any code or architectural work
- If the project file doesn't exist, suggest running the /project-init skill to create it.

## SCOPE DISCIPLINE

- Every changed line must trace directly to the user's request
- Fix what was asked. Do not refactor, rename, or "clean up" adjacent code
- If you notice something worth fixing nearby, report it; do not fix it
- Match existing style, even if you would do it differently
- Clean up imports/variables/functions YOUR changes made unused. Do not remove pre-existing dead code
- When I say "don't change X" or "only change Y", that is a hard constraint. If unsure whether something is in scope, ask

## WORKFLOW

- Enter plan mode for any non-trivial task (3+ steps or architectural decisions)
- Name plan files descriptively in kebab-case. E.g., `add-input-validation.md`
- If something goes sideways, STOP and re-plan. Do not keep pushing
- When in doubt, ask for a plan outline before writing the full plan
- Docs, specs, and design notes go in [.project/docs/](docs)
- Research reports and deep dives go in [.project/docs/research/](docs/research)
- Bug fixes and failing CI: investigate and fix without asking for permission. Plan mode still applies if the fix spans 3+ files

### Research Before Implementation

- Check available skills and MCP tools first (context7, microsoft-docs)
- When working with any framework/library API, verify current signatures using official docs. Do not assume from memory
- For decision points with 2+ valid approaches, present options and ask
- If research shows the user's approach has issues, present the findings and ask. Do not silently use a deprecated or broken API

### Delegation Bias

- You default to doing everything inline. Fight this!
- ALWAYS delegate research, exploration, and verification to subagents. They exist to keep your context clean and focused on high-level decisions
- If a task involves reading 3+ files to answer a question, that's a subagent
- If two tasks are independent, run them in parallel subagents
- Only use agent teams when workers need to coordinate or own distinct files across a multi-step plan

### Before Starting

- Build the full solution (not just the project you will edit) and run relevant tests. Both must pass.
- If anything is already broken, report it to the user before proceeding

### Before Done (skip mechanical checks for single-line fixes, typos, and config changes)

Never mark a task complete unless ALL hold:
- Build passes
- Tests pass (existing and new if behavior changed)
- Git diff reviewed with no unintended changes
- No TODO/HACK comments introduced without user approval
- No workarounds. If the fix suppresses warnings, adds special cases, or stacks 2+ workarounds, stop and redesign

## FOLLOW EXACTLY

- When the user provides code, signatures, or names, use their exact pattern. Do not "improve" or abstract
- When told "X is wrong", fix X precisely. Do not overcorrect by swinging to the opposite extreme
- When a constraint is a number (e.g., 120 char line limit), measure it. Do not guess or round down aggressively
- Only fix what actually violates the constraint; leave the rest alone

## COMMUNICATION

- Always explain the "why", not just the "what". In commit messages, plan updates, code comments, and conversation
- When flagging issues, always include: the file, the line, and the exact change needed
- When a plan includes updating a tracking document (SITREP.md, INDEX.md, changelog), update it as part of the same workflow. Do not wait to be reminded

## FILE EDITING

### Preserve Content During Refactoring

- When refactoring large sections of a file, use `Edit` for targeted changes rather than `Write` to rewrite the entire file.
- Using `Write` to replace the whole file from scratch loses details not in immediate context: inline comments, subtle formatting, edge case examples, or instructional notes scattered throughout.
- Prefer: Read → Use Edit for surgical changes → Details preserved
- Avoid: Read → Write entire new version → Details lost
- This applies especially to documentation files with inline code comments and markdown files with formatting nuances.

## MARKDOWN

- Before writing or editing any `.md` file, run /markdown-style skill to load formatting conventions
- This applies to all markdown: plans, docs, templates, skill files, scratch files, PR descriptions

## CODE STYLE

### Design Preferences

- Simple constructs > Complex patterns (functions > factories, closures > builders)
- Existing types > New wrapper types
- Built-in API features > Custom abstractions
- Before creating any new type: check what already exists, check if the framework API has an overload that handles it
- Build after each logically complete edit batch. Do not stack unbuildable changes
- No error handling for scenarios that cannot occur
- Do not write 200 lines when 50 solve the problem. Do not rewrite existing code to achieve brevity unless requested

### Machine-Generated Code

Files produced by code generators, schema compilers, or build tools
(e.g., protobuf, OpenAPI, ORM scaffolding, source generators):

- Never modify, rename into, or manually edit these files
- When renaming types, verify new names do not collide with generated member names
- If the output is wrong, fix the source (schema, spec, generator config), not the output
