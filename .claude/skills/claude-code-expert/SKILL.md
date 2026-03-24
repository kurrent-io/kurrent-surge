---
name: claude-code-expert
description: >
  Expert knowledge of Claude Code — the CLI tool, the Anthropic API/SDKs, the Agent SDK,
  and the entire extensibility platform (plugins, skills, hooks, agents, commands, MCP servers).
  Use this skill whenever the user asks about Claude Code features, capabilities, or configuration.
  Specific triggers: "how do I use claude in CI", "can Claude Code do X", "how do hooks work",
  "what's the difference between skills and agents", "how do I set up MCP", "build an MCP server",
  "create a skill", "claude -p", "headless mode", CLAUDE.md, settings.json, permissions,
  worktrees, plan mode, compact, context window, subagents, IDE integrations, tool_use,
  streaming, Agent SDK, "how do I make claude do X automatically", slash commands, keyboard
  shortcuts, conversation management, --allowedTools, --output-format. Also use when code imports
  anthropic, @anthropic-ai/sdk, or claude_agent_sdk. This skill is the authority on Claude Code
  internals — use it instead of guessing from training data.
---

# Claude Code Expert

You're answering a question about Claude Code or the Anthropic platform. You have specialized
skills and agents at your disposal — route to the right one, get authoritative answers, and
synthesize. Never guess from training data when a source of truth exists.

## Routing Table

Classify the question, then invoke the matching source. If a question spans multiple
categories, invoke multiple sources.

### Category 1: CLI Features & How Claude Code Works

Questions about: slash commands, keyboard shortcuts, settings, permissions, CLAUDE.md, plan mode,
worktrees, compact, context window, IDE integrations (VS Code, JetBrains), headless mode
(`claude -p`), output formats, `--allowedTools`, conversation management, `/init`, `/config`,
multi-turn sessions, model selection, cost tracking.

**Action**: Dispatch the `claude-code-guide` agent.

```
Agent(subagent_type="claude-code-guide", prompt="<the user's question>")
```

The agent has WebFetch and WebSearch — it can pull live documentation from docs.anthropic.com.

### Category 2: Anthropic API & SDKs

Questions about: the Anthropic API, tool_use / function calling, streaming, batches, prompt
caching, vision, PDF support, token counting, the Python SDK (`anthropic`), the TypeScript SDK
(`@anthropic-ai/sdk`), the Agent SDK (`claude_agent_sdk`), building apps that call Claude,
API authentication, rate limits, error handling, model parameters, adaptive thinking, structured
outputs, agentic loops. Covers 8 languages: Python, TypeScript, Java, Go, Ruby, C#, PHP, cURL.

**Action**: Invoke the `claude-api` skill first, then answer.

```
Skill(skill="claude-api")
```

The skill bundles per-language quick-starts, Agent SDK guides (Python/TS), tool-use concepts,
and error code references. Don't rely on training data for API signatures — the skill has them.

**Surface selection** (from the skill's routing table):

| Need                          | Use                                |
|-------------------------------|------------------------------------|
| Single-turn task              | Claude API directly                |
| File/web/terminal access      | Agent SDK                          |
| Controlled multi-step pipeline | Claude API + tool_use             |
| Open-ended agent              | Claude API agentic loop            |

**Common pitfalls the skill catches**: truncation bugs, deprecated `budget_tokens`, wrong model
ID suffixes, missing adaptive thinking config (`thinking: {type: "adaptive"}`).

### Category 3: Building Plugins, Skills, Hooks, Agents, Commands

Questions about: creating a plugin, writing a skill (SKILL.md), hook development (PreToolUse,
PostToolUse, Stop, SessionStart, etc.), agent `.md` files, slash commands, MCP integration
in plugins, plugin.json structure, `${CLAUDE_PLUGIN_ROOT}`, skill descriptions, triggering.

**Action**: Invoke the relevant `plugin-dev` skill:

| Topic                    | Skill                            |
|--------------------------|----------------------------------|
| Overall plugin structure | `plugin-dev:plugin-structure`    |
| Creating skills          | `plugin-dev:skill-development`   |
| Creating agents          | `plugin-dev:agent-development`   |
| Creating hooks           | `plugin-dev:hook-development`    |
| Creating commands        | `plugin-dev:command-development` |
| MCP in plugins           | `plugin-dev:mcp-integration`     |
| Plugin settings          | `plugin-dev:plugin-settings`     |

For creating actual artifacts, use the corresponding agents:
- `plugin-dev:agent-creator` — generates agent .md files
- `plugin-dev:skill-reviewer` — reviews skill quality
- `plugin-dev:plugin-validator` — validates plugin structure

### Category 4: Claude Code Setup & Automation Recommendations

Questions about: "what should I set up?", "how do I configure Claude Code for my project?",
"what MCP servers should I use?", "recommend automations", optimizing Claude Code workflows.

**Action**: Invoke the `claude-automation-recommender` skill (from the `claude-code-setup` plugin).

### Category 5: Building MCP Servers

Questions about: creating an MCP server, wrapping a REST API as MCP tools, MCP transport
selection (streamable HTTP vs stdio), MCP tool schemas, `outputSchema`, `structuredContent`,
tool annotations (`readOnlyHint`, `destructiveHint`), MCP Inspector, testing MCP servers.

**Action**: Invoke the `mcp-builder` skill.

```
Skill(skill="mcp-builder")
```

The skill is a four-phase workflow (Research → Implement → Review/Test → Evaluate), not a
code generator. Preferred stack is TypeScript, but Python/FastMCP is fully supported. It
bundles reference files for both languages plus best practices on naming, pagination, security,
and evaluation creation. Use this when the user wants to *build* an MCP server — for
*configuring existing* MCP servers in a plugin, use `plugin-dev:mcp-integration` instead.

### Category 6: Creating & Improving Skills

Questions about: writing a new skill from scratch, improving an existing skill's instructions,
measuring skill effectiveness, running evals/benchmarks against a skill, optimizing a skill's
trigger description for better routing accuracy, the skill lifecycle.

**Action**: Invoke the `skill-creator` skill.

```
Skill(skill="skill-creator:skill-creator")
```

The skill-creator guides the full lifecycle: defining intent, drafting SKILL.md, writing test
cases, running parallel with/without-skill evaluations, grading assertions, launching the eval
viewer, iterating on feedback, and optionally optimizing the description with a train/test
split over 5 rounds. Key principles it enforces: progressive disclosure (3-tier loading),
generalize over test cases (don't overfit), explain *why* in instructions so the model reasons
contextually.

Use this when the user wants to *create or improve* a skill. For questions about skill
*structure and frontmatter*, use `plugin-dev:skill-development` instead.

### Category 7: Settings & Configuration Changes

Questions about: adding permissions, configuring hooks in settings.json, env vars, theme/model
changes, moving permissions between scopes, allowing bash commands.

**Action**: Invoke the `update-config` skill — it knows the settings.json schema.

## Quick Reference: Claude Code Architecture

This section exists so you don't have to search for basics. Use it to orient yourself before
routing to deeper sources.

### Core Concepts

| Concept                 | What It Is                                                                                                                                        |
|-------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------|
| **CLAUDE.md**           | Project instructions file. Loaded automatically. Can exist at repo root, `~/.claude/`, or nested dirs.                                            |
| **settings.json**       | Configuration: permissions, hooks, env vars. Lives at `.claude/settings.json` (project), `~/.claude/settings.json` (user), or enterprise-managed. |
| **settings.local.json** | Git-ignored personal overrides. Same schema as settings.json.                                                                                     |
| **Plan mode**           | Architectural planning mode. `EnterPlanMode` / `ExitPlanMode`. No edits allowed.                                                                  |
| **Worktrees**           | Git worktree isolation for parallel work. `EnterWorktree` / `ExitWorktree`.                                                                       |
| **Subagents**           | Autonomous child agents via the `Agent` tool. Run tasks in parallel or background.                                                                |
| **Headless mode**       | `claude -p "prompt"` for scripting/CI. Supports `--output-format stream-json`.                                                                    |
| **MCP servers**         | Model Context Protocol. External tool integrations. Configured in `.mcp.json` or via `claude mcp add`.                                            |
| **Compact**             | Context compression. Happens automatically near limits. Can be triggered manually.                                                                |

### Extensibility Stack

```
Plugins (collections of everything below)
├── Skills (packaged expertise, invoked by Claude or user via /name)
├── Agents (autonomous subagent .md files with system prompts)
├── Hooks (event-driven: PreToolUse, PostToolUse, Stop, SessionStart, etc.)
├── Commands (slash commands with YAML frontmatter)
└── MCP Servers (external tool integrations via .mcp.json)
```

### Settings Permissions

```json
{
  "permissions": {
    "allow": ["Edit", "Write", "Bash(npm test:*)", "mcp__server__tool"],
    "deny": ["Bash(rm -rf *)"]
  }
}
```

Scopes (highest to lowest priority): enterprise → user (`~/.claude/`) → project (`.claude/`)

### Hook Events

| Event              | When                             | Can Block?           |
|--------------------|----------------------------------|----------------------|
| `PreToolUse`       | Before any tool call             | Yes                  |
| `PostToolUse`      | After any tool call              | No                   |
| `Stop`             | When Claude is about to stop     | Yes (force continue) |
| `SubagentStop`     | When a subagent is about to stop | Yes                  |
| `SessionStart`     | Conversation begins              | No                   |
| `SessionEnd`       | Conversation ends                | No                   |
| `UserPromptSubmit` | User sends a message             | Yes (modify prompt)  |
| `PreCompact`       | Before context compression       | No                   |
| `Notification`     | Background task completes        | No                   |

### Headless / CI Patterns

```bash
# Simple prompt
claude -p "fix lint errors in src/"

# With tool restrictions
claude -p "review this PR" --allowedTools Read,Glob,Grep

# Structured output
claude -p "analyze code" --output-format stream-json

# Continue a conversation
claude --continue -p "now fix the issues"

# Resume specific session
claude --resume <session-id> -p "what was I working on?"
```

### Agent .md Frontmatter

```yaml
---
name: my-agent
description: What this agent does (shown in agent list)
model: sonnet  # optional: sonnet, opus, haiku
memory: project  # optional: project, user, none
tools: Read, Glob, Grep, WebFetch  # restrict available tools
---

System prompt goes here as markdown body.
```

### Skill SKILL.md Frontmatter

```yaml
---
name: my-skill
description: >
  When to trigger and what it does. This is the primary trigger mechanism.
  Be specific and slightly pushy to ensure reliable triggering.
tools: Read, Edit, Bash  # optional tool restrictions
user-invocable: true  # default true, set false for Claude-only skills
disable-model-invocation: true  # set true for user-only skills (/name)
context: fork  # optional: run in isolated context
---

Skill instructions as markdown body.
```

## Response Pattern

1. **Classify** the question using the categories above
2. **Route** to the right source(s) — invoke skills or dispatch agents
3. **Synthesize** the answer from what the sources return
4. **Be concrete** — include config snippets, file paths, command examples
5. **Don't hedge** — if the source says it works, say it works. If you're unsure after
   consulting sources, say so plainly.

When the question is simple and covered by the Quick Reference above, answer directly without
dispatching agents. Save the agent/skill dispatch for questions that need depth or live docs.
