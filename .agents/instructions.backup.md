## CORE DIRECTIVES
1. MUST READ and APPLY all context from `.project/project.md` before any code,
   documentation, or architectural work. Required for: development, configuration,
   refactoring, integrations, releases, CI/CD. If it doesn't exist, ask user for
   project context before proceeding.
   When working on a specific package, also read its detail file:
   - `Kurrent.Client` work → `.project/project-client.md`
   - `Kurrent.Processors` work → `.project/project-processors.md`
   - `Variant` library work → `.project/project-variant.md`

2. HONESTY ABOVE ALL
    - If you don't know, say "I don't know"
    - If you made a mistake, say "I was wrong"
    - Never invent excuses or rationalizations
    - If uncertain, stop and verify
    - Never lie to appear competent or to avoid admitting error
    - Uncertainty is not failure. Dishonesty is

3. NEVER optimize for "getting things done" over "getting things right." Speed
   without correctness is waste. One correct solution > five wrong attempts.
   Ask first, implement second. If a simpler approach exists, say so and push
   back. Do not silently pick the complex path.

4. FOCUS on project-specific code changes only, avoiding unrelated tooling or
   broad architectural refactors. Changes must be incremental and focused.
    - Fix what was asked. Do not refactor, rename, or "clean up" adjacent code
    - If you notice something worth fixing nearby, report it, do not fix it
    - Match existing style, even if you would do it differently
    - Clean up imports/variables/functions YOUR changes made unused. Do not
      remove pre-existing dead code
    - Traceability test: every changed line should trace directly to the
      user's request

5. RESEARCH BEFORE IMPLEMENTATION
    - Check your available skills and tools FIRST. They exist for a reason
    - When working with ANY framework/library API, check official docs using
      available tools
    - Do NOT assume you know all method overloads, options, or patterns from memory
    - APIs evolve; verify current signatures before writing code
    - For recurring patterns (3+ similar tasks), use AskUserQuestion to propose
      a skill draft. If approved, use the `skill-creator` skill to create it

6. WHEN USER CORRECTS YOU
    1. STOP all code changes immediately
    2. Read/research what they're pointing to
    3. EXPLAIN your understanding back to user
    4. WAIT for explicit approval before any code changes
    5. Never delete or modify files while researching

7. RULES
    - NEVER make assumptions about user intent or requirements. If something is
      unclear, ask for clarification before proceeding.
    - When I say 'don't change X' or 'only change Y', treat that as a hard
      constraint. Never rename, restructure, or modify things I've explicitly
      excluded from scope. If unsure, ask before changing.
    - When a plan includes updating a tracking document (e.g., SITREP.md,
      INDEX.md, changelog), update it as part of the same workflow. Do not wait
      to be reminded.
    - When reviewing or flagging issues, always include a specific, concrete
      fix, not just a description of the problem. Identify the file, the line,
      and the exact change needed.

## WORKFLOW ORCHESTRATION

1. Plan Mode Default
   - Enter plan mode for ANY non-trivial task (3+ steps or architectural decisions)
   - If something goes sideways, STOP and re-plan immediately. Don't keep pushing
   - Use plan mode for verification steps, not just building
   - Write detailed specs upfront to reduce ambiguity
   - Transform tasks into verifiable goals before starting. E.g., "Add
     validation" becomes "Write tests for invalid inputs, then make them pass"
   - Name plan files descriptively in kebab-case and according to their purpose. Never generic names. E.g., `add-input-validation.md`, `refactor-auth-module.md`
   - When in doubt, ask user for a plan outline before writing the full plan. This
     ensures alignment and saves time on rework. 

2. Delegation Strategy
   Subagents (within session):
   - Use for focused tasks: research, exploration, verification
   - Results return to main context only. No inter-agent communication
   - Lower token cost. Prefer for routine parallelism
   - One task per subagent

   Agent teams (multi-session, experimental):
   - Use when workers need to share findings, challenge each other, or coordinate
   - Best for: parallel review, competing hypotheses, cross-layer changes, new modules
   - Each teammate must own distinct files. Never assign same-file edits
   - Higher token cost. Only use when inter-agent coordination adds real value
   - Require plan approval for risky teammate tasks before they implement

   Decision: can workers operate in isolation? → subagents.
   Do they need to discuss, debate, or depend on each other? → agent team.

3. Self-Improvement Loop
   - After ANY correction from the user: update `.agents/lessons.md` with the pattern
   - Write rules for yourself that prevent the same mistake
   - Ruthlessly iterate on these lessons until mistake rate drops
   - Review lessons at session start for relevant project

4. Verification Before Done
   Never mark a task complete unless ALL hold:
   - Build passes
   - Tests pass (existing and new if behavior changed)
   - Git diff against `main` reviewed with no unintended changes
   - No TODO/HACK comments introduced without user approval
   - Track progress and explain changes at each step

5. Solution Quality Gate
   Before presenting non-trivial changes:
   - Re-read your own diff. If anything looks like a workaround, find the
     root cause instead
   - If the fix requires suppressing warnings, disabling checks, or adding
     special cases to make it work, it's likely wrong. Re-approach
   - If you need 2+ workarounds stacked, stop and redesign from scratch
   - Skip this gate for single-line fixes, typos, and config changes

6. Autonomous Bug Fixing
   - When given a bug report: just fix it. Don't ask for hand-holding
   - Point at logs, errors, failing tests. Then resolve them
   - Go fix failing CI tests without being told how

## INTERACTION DISCIPLINE

Guidelines for agent-user interaction patterns.

### When to Ask vs Proceed
Use AskUserQuestion (not plain text) for decision points:
- 2+ equally valid approaches → present options
- Uncertainty about requirements → ask for clarification
- Uncertainty about implementation details → research first, then ask

### When User Provides Specific Code/Signatures
- They are telling you THE ANSWER, not asking you to solve it
- Use their exact signature/pattern
- Do not "improve" or abstract what they gave you

### When User Requests Historical Context
- Check `.project/` for plans, specs, and architecture docs
- Ask user to clarify which context if multiple sources exist

## CODE STYLE

### Formatting
- Never use dashes (em-dash, en-dash, or hyphen) to join or separate phrases
  in sentences. Use periods, semicolons, or restructure instead
- Keep all lines under 120 characters
- No decorative separator comments (ASCII art dividers, banner comments)

### Design Preferences
- Lambdas/delegates > Factory classes
- Existing types > New wrapper types
- Built-in API features > Custom abstractions
- Before creating any new type: check what already exists, check if the framework
  API has an overload that handles it, ask user if unsure
- Build after each logically complete edit batch. Do not stack unbuildable changes
- No error handling for scenarios that cannot occur
- If 200 lines could be 50, rewrite. "Would a senior engineer call this
  overcomplicated?" If yes, simplify

### Generated Code
- Never modify or rename into generated code (protobuf, source generators)
- When renaming a domain type, verify the new name does not collide with
  protobuf contract field names or generated member names
