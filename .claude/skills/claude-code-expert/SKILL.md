---
name: claude-code-expert
description: >
  Expert knowledge of Claude Code — the CLI tool, the Anthropic API/SDKs, the Agent SDK, and the entire
  extensibility platform (plugins, skills, hooks, agents, commands, MCP servers). Use this skill whenever the user
  asks about Claude Code features, capabilities, or configuration. Specific triggers: "how do I use claude in CI",
  "can Claude Code do X", "how do hooks work", "what's the difference between skills and agents", "how do I set up
  MCP", "build an MCP server", "create a skill", "claude -p", "headless mode", CLAUDE.md, settings.json,
  permissions, worktrees, plan mode, compact, context window, subagents, IDE integrations, tool_use, streaming,
  Agent SDK, "how do I make claude do X automatically", slash commands, keyboard shortcuts, conversation management,
  --allowedTools, --output-format. Also use when code imports anthropic, @anthropic-ai/sdk, or claude_agent_sdk.
  This skill is the authority on Claude Code internals — use it instead of guessing from training data.
---

# Claude Code Expert

You're answering a question about Claude Code or the Anthropic platform. You have specialized skills and agents at
your disposal — route to the right one, get authoritative answers, and synthesize. Never guess from training data
when a source of truth exists.

## Feature Landscape

Answer "which feature should I use?" questions directly from these tables — no need to dispatch.

### Features and their purpose

| Feature        | What it does                                     | When to use it                                 |
|----------------|--------------------------------------------------|------------------------------------------------|
| **CLAUDE.md**  | Persistent context loaded every session          | Project conventions, "always do X" rules       |
| **Rules**      | Path-scoped instructions in `.claude/rules/`     | Language- or directory-specific guidelines     |
| **Skill**      | On-demand knowledge, reference docs, workflows   | Reusable content, repeatable tasks via `/name` |
| **Subagent**   | Isolated worker that returns summarized results  | Context isolation, parallel tasks              |
| **Agent team** | Multiple independent sessions with shared tasks  | Complex parallel work requiring coordination   |
| **MCP**        | Connect to external services                     | External data or actions (DB, Slack, browser)  |
| **Hook**       | Deterministic script on lifecycle events         | Predictable automation, no LLM involved        |
| **Plugin**     | Bundle of skills, hooks, agents, and MCP servers | Reuse across repos, distribute via marketplace |

### Context costs

Every extension consumes context. This matters for recommendations.

| Feature       | When it loads                      | Context cost                                                |
|---------------|------------------------------------|-------------------------------------------------------------|
| **CLAUDE.md** | Session start                      | Full content, every request                                 |
| **Rules**     | Session start / on file access     | Scoped — only matching paths load                           |
| **Skills**    | Descriptions at start, full on use | Low until invoked; zero if `disable-model-invocation: true` |
| **MCP**       | Tool names at start                | Low until a tool is used (schemas deferred)                 |
| **Subagents** | When spawned                       | Isolated — doesn't consume main session context             |
| **Hooks**     | On trigger                         | Zero (runs externally)                                      |

**Key tradeoff**: CLAUDE.md is always-on but costs every request. Skills are on-demand but cost nothing until used.

## Routing Table

Classify the question, then invoke the matching source. If a question spans multiple categories, invoke multiple
sources.

### Category 1: CLI Features & How Claude Code Works

Questions about: slash commands, keyboard shortcuts, settings, permissions, CLAUDE.md, `.claude/rules/`, plan mode,
worktrees, compact, context window, IDE integrations (VS Code, JetBrains), headless mode (`claude -p`), output
formats, `--allowedTools`, conversation management, `/init`, `/config`, multi-turn sessions, model selection, cost
tracking, agent teams, subagent scope, context loading.

**Action**: Dispatch the `claude-code-guide` agent. It has WebFetch and WebSearch — it can pull live
documentation from docs.anthropic.com.

### Category 2: Building Plugins, Skills, Hooks, Agents, Commands

Questions about: creating a plugin, writing a skill (SKILL.md), hook development, agent `.md` files, slash
commands, MCP integration in plugins, plugin.json structure, `${CLAUDE_PLUGIN_ROOT}`, skill descriptions,
triggering.

**Action**: Invoke the appropriate `plugin-dev:*` skill. The `plugin-dev` skill set covers plugin structure, skills,
agents, hooks, commands, MCP integration, and plugin settings — each with its own skill. Match the topic to the
right one.

**When `plugin-dev` is not available**: say so, and route to what is: skill authoring →
`skill-creator:skill-creator`; hook wiring in settings.json → `update-config`; plugin/agent structure concepts →
the `claude-code-guide` agent. Do not enable or install the plugin on your own — if it appears deliberately
disabled in a settings scope, that is a decision to surface, not override (see Fallbacks).

### Category 3: Claude Code Setup & Automation Recommendations

Questions about: "what should I set up?", "how do I configure Claude Code for my project?", "what MCP servers
should I use?", "recommend automations", optimizing Claude Code workflows.

**Action**: Invoke the `claude-automation-recommender` skill (from the `claude-code-setup` plugin).

**When it is not available**: use the closest equivalents present in the session: `session-retrospective` —
mines past sessions for recurring friction and proposes vetted interventions (rules, skills, hooks, subagents,
CLAUDE.md edits); `find-skills` — discovers installable skills for a capability gap. If neither fits, dispatch
the `claude-code-guide` agent for setup guidance from live docs.

### Category 4: Building MCP Servers

Questions about: creating an MCP server, wrapping a REST API as MCP tools, MCP transport selection (streamable HTTP
vs stdio), MCP tool schemas, `outputSchema`, `structuredContent`, tool annotations (`readOnlyHint`,`destructiveHint`)
, MCP Inspector, testing MCP servers.

**Action**: Invoke the `mcp-builder` skill. It's a four-phase workflow (Research → Implement → Review/Test →
Evaluate), not a code generator. Preferred stack is TypeScript, but Python/FastMCP is fully supported. Use this when
the user wants to *build* an MCP server — for *configuring existing* MCP servers in a plugin, use
`plugin-dev:mcp-integration` instead.

**When `mcp-builder` is not available**: say so and offer to install it, or fall back to the
`claude-code-guide` agent (it can WebFetch the MCP spec and SDK docs at modelcontextprotocol.io).

### Category 5: Creating & Improving Skills

Questions about: writing a new skill from scratch, improving an existing skill's instructions, measuring skill
effectiveness, running evals/benchmarks against a skill, optimizing a skill's trigger description for better routing
accuracy, the skill lifecycle.

**Action**: Invoke the `skill-creator:skill-creator` skill. Use this when the user wants to *create or improve* a
skill. For questions about skill *structure and frontmatter*, prefer `plugin-dev:skill-development`; when it is
not available, `skill-creator:skill-creator` covers it adequately. For skills targeting Microsoft technologies
specifically, `microsoft-docs:microsoft-skill-creator` scaffolds them grounded in official docs.

### Category 6: Settings & Configuration Changes

Questions about: adding permissions, configuring hooks in settings.json, env vars, theme/model changes, moving
permissions between scopes, allowing bash commands.

**Action**: Invoke the `update-config` skill — it knows the settings.json schema. Two specialized neighbors:
`keybindings-help` for keyboard shortcut customization (`~/.claude/keybindings.json`, rebinding, chords), and
`fewer-permission-prompts` for building a permission allowlist from transcript analysis instead of adding
permissions one by one.

### Category 7: Build apps with the Claude API or Anthropic SDKs

Questions about: the Anthropic API, tool_use / function calling, streaming, batches, prompt caching, vision, PDF
support, token counting, the Python SDK (`anthropic`), the TypeScript SDK (`@anthropic-ai/sdk`), the Agent SDK
(`claude_agent_sdk`), building apps that call Claude, API authentication, rate limits, error handling, model
parameters, adaptive thinking, structured outputs, agentic loops. Covers 8 languages: Python, TypeScript, Java, Go,
Ruby, C#, PHP, cURL.

**Action**: Invoke the `claude-api` skill first, then answer. It bundles per-language quick-starts, Agent SDK
guides (Python/TS), tool-use concepts, and error code references. Don't rely on training data for API
signatures — the skill has them.

## Fallbacks & Live Documentation

If a routed skill or plugin isn't available, don't silently skip — tell the user what's missing and ask permission
to install it. To install, run `claude plugin install <name>@<marketplace>` via Bash, or enable an already-cached
plugin by adding it to `.claude/settings.local.json` (gitignored, safe for local changes). After installing, run
`/reload-plugins` to activate changes in the current session.

Availability is session state, not a fact this file can assert — check the live skill and agent inventory in the
current session before routing. A plugin explicitly set to `false` in a settings scope may be a deliberate team
control: report it and ask, never flip it unilaterally. Installing or enabling anything is always the user's call.

Authoritative live sources when skills don't have the answer:

- **Features overview**: https://code.claude.com/docs/en/features-overview
- **How it works**: https://code.claude.com/docs/en/how-claude-code-works
- **Changelog**: https://code.claude.com/docs/en/changelog — features land here before they make it into skills
  or training data

## Response Pattern

1. **Classify** the question using the categories above
2. **Route** to the right source(s) — invoke skills or dispatch agents
3. **Synthesize** the answer from what the sources return
4. **Be concrete** — include config snippets, file paths, command examples
5. **Don't hedge** — if the source says it works, say it works. If you're unsure after consulting sources, say so
   plainly.
