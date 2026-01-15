---
description: Auto-detect prompt type and rate quality against official specs and type-specific criteria
argument-hint: <prompt-text or @file> [--type:skill|command|agent]
---

# Rank Prompt

Evaluate any prompt's quality - auto-detect whether it's a skill, command, or agent, validate against official Claude Code specs, and rate using type-specific criteria. Be constructive: find weaknesses, suggest fixes, output an improved version.

> **Skills evaluated against**: [Official Skill Best Practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)

## Input Parsing

The user provides `$ARGUMENTS` - either inline prompt text or a file reference (`@path/to/file.md`).

**Extract:**
- Prompt content (required)
- `--type:X` override (optional) - forces type instead of auto-detection

## Step 1: Auto-Detect Type

Analyze the prompt and detect type using official patterns:

| Type        | Detection Signals                                                                                                                |
|-------------|----------------------------------------------------------------------------------------------------------------------------------|
| **Skill**   | Mentions `SKILL.md`, `.claude/skills/`, frontmatter with `name`+`description`, "expert-level", knowledge areas, trigger keywords |
| **Command** | Mentions `$ARGUMENTS`, `.claude/commands/`, frontmatter with `description`+`argument-hint`, step-based flow                      |
| **Agent**   | Mentions `.claude/agents/`, frontmatter with `tools`+`permissionMode`, system prompt patterns, delegation language               |
| **General** | None of the above                                                                                                                |

If `--type:X` provided, skip detection and use specified type.

Output: `Detected type: {type}` (or `Forced type: {type}` if overridden)

## Step 2: Spec Validation

For Skill, Command, or Agent types, validate against official specs:

### Skill Spec

**Frontmatter validation:**

| Check           | Rule                                                     |
|-----------------|----------------------------------------------------------|
| `name`          | Required; max 64 chars; lowercase, numbers, hyphens only |
| `description`   | Required; max 1024 chars; third person; what + when      |
| `allowed-tools` | Optional; comma-separated tool names                     |
| `model`         | Optional; valid model identifier                         |

**Description rules (critical for discovery):**

- ✅ Third person ONLY ("Processes files" not "I help you" or "You can use this")
- ✅ Must include BOTH what it does AND when to use it
- ✅ Include specific key terms and trigger phrases
- ❌ No vague descriptions ("Helps with documents")

**Good:** `"Extract text from PDFs, fill forms. Use when working with PDF files or when user mentions PDFs, forms, or document extraction."`

**Structure constraints:**

| Constraint       | Limit                                        |
|------------------|----------------------------------------------|
| SKILL.md body    | Under 500 lines                              |
| Reference depth  | One level deep from SKILL.md only            |
| Naming style     | Gerund preferred (`processing-pdfs`)         |
| File paths       | Forward slashes only (no Windows backslashes)|

### Command Spec

| Check           | Rule                                             |
|-----------------|--------------------------------------------------|
| `description`   | Required for `/help` visibility                  |
| `argument-hint` | Optional; shows expected args                    |
| `allowed-tools` | Optional; comma-separated with optional patterns |
| `model`         | Optional; valid model identifier                 |

### Agent Spec

| Check            | Rule                                                                                 |
|------------------|--------------------------------------------------------------------------------------|
| `name`           | Required; unique lowercase with hyphens                                              |
| `description`    | Required; used for auto-delegation                                                   |
| `tools`          | Optional; comma-separated tool names                                                 |
| `model`          | Optional; `inherit`, alias, or full identifier                                       |
| `permissionMode` | Optional; `default`, `acceptEdits`, `dontAsk`, `bypassPermissions`, `plan`, `ignore` |
| `skills`         | Optional; comma-separated skill names                                                |

Output any spec violations as warnings.

## Step 3: Rate Quality

Rate each criterion 1-5 (1=poor, 5=excellent). Use the appropriate criteria based on detected type.

### Common Criteria (all types)

| Criterion          | Question                                                      |
|--------------------|---------------------------------------------------------------|
| Clarity            | Is the guidance unambiguous and easy to follow?               |
| Actionable         | Can someone execute this without asking clarifying questions? |
| Context sufficient | Is enough background/context provided?                        |

### Type-Specific Criteria

**Skill** - Official Checklist (+ common = 14 total)

*Core Quality:*

| Criterion              | Question                                                                        |
|------------------------|---------------------------------------------------------------------------------|
| Description quality    | Third person? What + when? Specific key terms? (max 1024 chars)                 |
| Body length            | SKILL.md body under 500 lines?                                                  |
| Progressive disclosure | Essential info upfront, details in separate reference files?                    |
| Reference depth        | All references one level deep from SKILL.md? (no nested refs)                   |
| Time-sensitivity       | No time-sensitive info? (or in "old patterns" section)                          |
| Terminology            | Consistent terminology throughout? (no mixing "API endpoint"/"URL"/"route")     |
| Examples               | Concrete examples, not abstract?                                                |
| Workflows              | Clear steps for multi-step processes?                                           |
| Conciseness            | Only adds context Claude doesn't already have? ("Does Claude need this?")       |
| Degrees of freedom     | Specificity matches task fragility? (high freedom for flexible, low for fragile)|
| Reference file TOC     | Do reference files >100 lines have a table of contents at the top?              |

*Code/Scripts (if applicable):*

| Criterion              | Question                                                                        |
|------------------------|---------------------------------------------------------------------------------|
| Scripts solve          | Scripts handle errors rather than punt to Claude?                               |
| No magic constants     | All values justified and documented? (no "voodoo constants")                    |
| Dependencies           | Required packages listed and verified available?                                |

**Command** (+ common = 6 total)

| Criterion      | Question                                               |
|----------------|--------------------------------------------------------|
| Clear flow     | Do the steps make logical sense and flow naturally?    |
| Input clarity  | Is `$ARGUMENTS` usage clear? Are placeholders obvious? |
| Output clarity | Is what the command produces well-defined?             |

**Agent** (+ common = 6 total)

| Criterion      | Question                                            |
|----------------|-----------------------------------------------------|
| Role clarity   | Is the agent's expertise and scope clearly defined? |
| Scope bounded  | Does it know when to stop and return results?       |
| Tool awareness | Are the right tools specified for the task?         |

**General** (+ common = 5 total)

| Criterion    | Question                                       |
|--------------|------------------------------------------------|
| Clear intent | Is the goal obvious within the first sentence? |
| Right detail | Not too vague, not over-specified?             |

### Advanced Considerations (Complex Prompts)

For prompts with 3+ steps, multiple sections, or mixed content (instructions + data + examples), also consider:

| Consideration         | Question                                                                 |
|-----------------------|--------------------------------------------------------------------------|
| XML structure         | Would XML tags improve clarity between instructions, examples, and data? |
| Parallelization       | Could independent steps run as concurrent subagents?                     |
| Context isolation     | Would confining messy data (logs, raw output) keep main thread clean?    |
| Domain specialization | Would expert subagents (Security, QA, Architecture) improve accuracy?    |
| Error containment     | Should failures be isolated to prevent session loss?                     |

Flag as suggestion when any of these would significantly improve the prompt.

### Skill Anti-Patterns (Auto-Fail)

For Skills, flag these as critical issues requiring immediate fix:

| Anti-Pattern            | Why It's Bad                                                |
|-------------------------|-------------------------------------------------------------|
| First/second person     | "I help you" or "You can use this" breaks discovery         |
| Vague description       | "Helps with documents" - Claude can't know when to trigger  |
| Body over 500 lines     | Context window pollution; split to references               |
| Nested references       | SKILL.md → a.md → b.md - Claude may only partial-read       |
| Time-sensitive info     | "After August 2025..." becomes wrong                        |
| Inconsistent terms      | Mixing "endpoint"/"URL"/"route" confuses Claude             |
| Windows paths           | `scripts\helper.py` breaks on Unix                          |
| Too many options        | "Use pypdf or pdfplumber or..." - provide a default         |
| Vague names             | `helper`, `utils`, `tools` - meaningless                    |
| Over-explaining basics  | Claude already knows what PDFs are                          |

## Step 4: Improvement Loop

**For any criterion ≤3:**
1. Explain what's weak
2. Suggest specific improvement
3. Show improved version of that part

**After improvements:** Re-rate the improved prompt. Max 5 iterations, then proceed.

## Step 5: Output

```markdown
## Prompt Analysis

**Type:** {detected/forced type}
**Spec violations:** {list or "None"}

## Ratings

| Criterion | Score | Notes |
|-----------|-------|-------|
| ...       | X/5   | ...   |

**Overall:** X/5

## Suggested Improvements

{numbered list of specific improvements}

## Improved Prompt

{full improved prompt if any criterion was ≤3}
```
