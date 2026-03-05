## Scope

- Every changed line must trace to the user's request
- Do not refactor, rename, or clean up adjacent code
- Report adjacent issues. Do not fix them
- Clean up imports/variables/functions YOUR changes made unused. Do not remove pre-existing dead code
- Treat explicit user constraints ("don't change X", "only change Y") as absolute. Ask before violating one
- Do not rewrite existing code for brevity unless requested

## Precision

- Use user-provided code, signatures, and names exactly. Do not improve or abstract them
- When told "X is wrong", fix only X. Do not invert logic or rewrite surrounding code
- When a constraint is numeric, measure exactly. Do not estimate
- Only fix lines that violate the constraint

## Workflow

- Enter plan mode for tasks with 3+ steps or architectural decisions
- Bug fixes and failing CI: fix without asking. Plan mode applies if spanning 3+ files
- Verify framework/library API signatures using available skills, MCP tools, or official docs. Do not assume from memory
- For architectural decisions with 2+ valid approaches, present options and ask
- If research shows the user's approach is incorrect, deprecated, or defective, present findings and ask

## Delegation

- Before starting any task, evaluate: would a team of agents produce better results than working alone?
- Default to delegation. Research, exploration, verification, and multi-file tasks go to subagents, not inline
- Run independent tasks in parallel
- Do work inline only when it is a single-file edit or a direct follow-up to something you just read

## Quality Gates

Skip for single-line fixes, typos, and config changes.

### Before Starting

- Build the full project, not just the package you will edit. Must pass
- If the build or tests already fail, report to user before proceeding

### During

- Build after each set of changes that should compile independently. Do not stack unbuildable changes

### Before Done

All must hold:

- Build passes
- Tests pass (existing and new if behavior changed)
- Git diff reviewed with no unintended changes
- No TODO/HACK comments without user approval
- No workarounds. If the fix suppresses warnings, adds special cases, or stacks 2+ workarounds, stop and redesign

## Communication

- Include rationale (why) in commit messages, plan updates, and code comments
- Flag issues with: file path, line number, description

## Code Discipline

- Existing types over new wrappers. Built-in API features over custom abstractions
- Before creating any new type, check if the framework API has an overload that handles it
- Never modify or edit machine-generated files. Fix the source, not the output
- When refactoring, use `Edit` for targeted changes, not `Write` to rewrite entire files
