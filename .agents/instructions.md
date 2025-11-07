---
description: Core project context, instructions, protocols and guidelines for the designated codebase. MUST APPLY this knowledge and rules when working on any code, documentation, or configuration files within the designated project.
alwaysApply: true
inclusion: always
---

# CORE DIRECTIVES
1. MUST READ and APPLY all context from `.project/project.md` before any code, documentation, or architectural work. Required for: development, configuration, refactoring, integrations, releases, CI/CD.
2. MAINTAIN high-quality, comprehensive output, optimized for large codebases and without abbreviation, unless specified otherwise.
3. PROACTIVELY use the `repomix-explorer` subagent for all codebase analysis tasks via Task tool, when available; provide analysis mission; utilize agent results; use `repomix-output.xml` packed output, unless regeneration is explicitly requested by the user.
4. ENFORCE code style and conventions by using the `csharp-expert` subagent for all C# code related tasks.
5. IDENTIFY technical skill gaps, communicate limitations, recommend skill-creator tool, and await authorization for out-of-scope requests.
6. FOCUS on project-specific code changes only, avoiding unrelated tooling or broad architectural refactors. Changes must be incremental and focused.
7. USE standardized emoji taxonomy when applicable. 
   - Core patterns: 🟢 success, 🟡 warning, 🔴 error, 🔄 in-progress, ✅ complete
   - Do not read the standards in advance. 
   - When relevant, see `.agents/emoji.instructions.md` for full taxonomy.
8. RESEARCH unfamiliar patterns before implementing:
   - Search codebase for examples
   - Verify architectural layer per `.project/project.md`
   - Request clarification when uncertain

**COMPLIANCE EXPECTATION:**
- These directives represent project standards.
- Agents MUST adhere to applicable directives yet within their platform/tooling capabilities.
- Partial compliance acceptable when full adherence is impossible.
- Human oversight required for verification.

## PLATFORM ADAPTATION
Agents should:
1. Operate within available tooling capabilities
2. Apply universal principles when specific tools unavailable
3. Request clarification for unsupported operations
4. Prioritize project standards over platform limitations

---

## CODE STYLE ENFORCEMENT
Mandatory standards (non-negotiable):
- K&R brace style, 4-space C# indentation, 2-space markup for everything else
- Test methods: snake_case naming

---
