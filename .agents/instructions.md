# CORE DIRECTIVES
1. MUST READ and APPLY all context from `.project/project.md` before any code, documentation, or architectural work. Required for: development, configuration, refactoring, integrations, releases, CI/CD.
2. PREFER `jetbrains-rider-mcp-expert` skill for C# code intelligence: compiler diagnostics, symbol renaming, code search, running tests, and understanding symbols. Provides real compiler integration over text matching. Fallback to native tools only when Rider MCP doesn't deliver.
3. SKILL BOUNDARIES AND GROWTH
   You ARE empowered to:
   - State uncertainty directly when codebase search yields nothing
   - Ask for examples or references when needed
   - Propose skill creation for recurring patterns (3+ similar tasks)

   Process: search codebase first → verify architectural layer per `.project/project.md` → if nothing, say so → for recurring needs, offer to draft a skill.

   Gaps are opportunities for skill creation, not failures.
4. FOCUS on project-specific code changes only, avoiding unrelated tooling or broad architectural refactors. Changes must be incremental and focused.

## SESSION INITIALIZATION
On session start:
1. Ensure CORE DIRECTIVES are loaded and applied

When user requests historical context:
- Check `.project/` for plans, specs, and architecture docs
- Check `_bmad-output/` for workflow artifacts and summaries
- Ask user to clarify which context if multiple sources exist

---

## CODE STYLE

### Defaults (all languages)
- K&R braces
- 2-space indentation

### C# / .NET
- 4-space indentation (overrides default)
- Test methods: `snake_case`

## .NET CONVENTIONS

### Nullability
Assumes `<Nullable>enable</Nullable>` (modern .NET standard):
- Non-nullable `T` = compiler-guaranteed → no runtime null checks
- Use `T?` only for: valid domain state OR perf optimization (TryGet, lazy init)
- If nullable disabled → null checks acceptable

---
