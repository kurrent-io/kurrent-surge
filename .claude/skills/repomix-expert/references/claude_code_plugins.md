# Claude Code Plugin Integration

Comprehensive guide to using Repomix via Claude Code plugins, including slash commands, optimization patterns, and integration strategies.

## Overview

Repomix provides three interaction levels for different use cases:

```
┌─────────────────────────────────────────────────────────────┐
│ LEVEL 1: Claude Code Slash Commands (RECOMMENDED)           │
│ ├─ /repomix-explorer:explore-local                          │
│ ├─ /repomix-commands:pack-local                             │
│ └─ Use for: 90% of interactive analysis tasks               │
├─────────────────────────────────────────────────────────────┤
│ LEVEL 2: MCP Tools (Programmatic Control)                   │
│ ├─ mcp__repomix__pack_codebase                              │
│ ├─ mcp__repomix__grep_repomix_output                        │
│ └─ Use for: Custom workflows, repeated analysis             │
├─────────────────────────────────────────────────────────────┤
│ LEVEL 3: CLI (Automation)                                   │
│ ├─ npx repomix --options                                    │
│ └─ Use for: CI/CD pipelines, scripts                        │
└─────────────────────────────────────────────────────────────┘
```

**Key Principle:** Start high-level (slash commands), drop to lower levels only when needed.

## Slash Command Reference

### /repomix-explorer:explore-local

**Purpose:** Natural language codebase exploration and analysis through AI agent.

**How It Works:**

1. Accepts natural language query about codebase
2. Automatically packs repository using `npx repomix@latest`
3. Uses Grep and Read tools to search packed output efficiently
4. Provides comprehensive analysis with context management
5. Returns focused answer without overwhelming context

**Syntax:**

```
/repomix-explorer:explore-local <natural language query>
```

**Examples:**

```bash
# Find authentication implementation
/repomix-explorer:explore-local Find all authentication logic in the codebase

# Understand error handling patterns
/repomix-explorer:explore-local How does error handling work across the application?

# Locate testing patterns
/repomix-explorer:explore-local What testing patterns and frameworks are used?

# Identify database interactions
/repomix-explorer:explore-local Show me all database query implementations

# Architecture analysis
/repomix-explorer:explore-local What is the overall architecture and how do components interact?
```

**Advantages:**

- **Context-efficient:** Agent manages token usage, prevents exhaustion
- **Intelligent search:** Understands intent, searches multiple patterns
- **Automated workflow:** No manual pack → grep → read steps
- **Focused results:** Returns relevant findings without full codebase dump

**When to Use:**

- ✅ Understanding new codebase
- ✅ Finding specific patterns or logic
- ✅ Architecture exploration
- ✅ Code pattern mining
- ✅ Learning how systems work
- ✅ Initial codebase orientation

**When NOT to Use:**

- ❌ Need specific output format (use MCP tools)
- ❌ Need to control compression settings (use MCP tools)
- ❌ Repeated packing with same settings (use MCP tools + config)
- ❌ CI/CD automation (use CLI)

**Configuration:**

Leverages `.repomixrc.json` if present for:
- Ignore patterns
- Security settings
- Default output format

**Performance:**

- First run: Packs codebase (~2-5 seconds for small-medium projects)
- Subsequent searches: Uses cached packed output (instant)
- Token usage: Minimal (agent retrieves only relevant sections)

---

### /repomix-commands:pack-local

**Purpose:** Pack local codebase with natural language instructions.

**How It Works:**

1. Accepts conversational packing directives
2. Interprets intent and maps to Repomix parameters
3. Executes `npx repomix@latest` with appropriate options
4. Returns packed output for analysis

**Syntax:**

```
/repomix-commands:pack-local <natural language packing instruction>
```

**Examples:**

```bash
# Pack with compression
/repomix-commands:pack-local Pack this project as XML with compression enabled

# Pack as markdown
/repomix-commands:pack-local Pack the codebase as markdown for documentation

# Pack specific directory
/repomix-commands:pack-local Pack only the src directory

# Pack with filtering
/repomix-commands:pack-local Pack TypeScript files only, exclude tests

# Pack for security audit
/repomix-commands:pack-local Pack with security scanning enabled, uncompressed

# Pack as JSON
/repomix-commands:pack-local Pack entire project as JSON format
```

**Advantages:**

- **Natural language interface:** No CLI syntax required
- **Intent interpretation:** Agent figures out correct options
- **Quick operation:** Single command for common packing scenarios
- **Configuration aware:** Uses `.repomixrc.json` defaults

**When to Use:**

- ✅ Quick one-off packing
- ✅ Don't remember exact CLI syntax
- ✅ Simple packing requirements
- ✅ Experimenting with formats
- ✅ Ad-hoc analysis preparation

**When NOT to Use:**

- ❌ Complex filtering requirements (use MCP tools)
- ❌ Need precise parameter control (use MCP tools)
- ❌ Repeated packing with same settings (use MCP tools + config)
- ❌ Programmatic integration (use MCP tools)
- ❌ CI/CD automation (use CLI)

**Configuration:**

Respects `.repomixrc.json` settings:
- Uses configured ignore patterns
- Inherits security settings
- Applies default output format unless overridden

**Performance:**

- Packing time: 2-5 seconds (small-medium projects)
- Interpretation overhead: Minimal (~1 second)
- Result: Packed output file ready for analysis

---

## Optimization Hierarchy

### Decision Tree

```
START: Need to work with codebase
    │
    ├─ Need to UNDERSTAND/EXPLORE codebase?
    │   └─ YES → Use /repomix-explorer:explore-local
    │       └─ "Find authentication logic"
    │       └─ "How does error handling work?"
    │
    ├─ Need to PACK for immediate analysis?
    │   └─ YES → Use /repomix-commands:pack-local
    │       └─ "Pack as markdown with compression"
    │
    ├─ Need CUSTOM/REPEATED packing workflow?
    │   └─ YES → Use MCP Tools + .repomixrc.json
    │       └─ mcp__repomix__pack_codebase with parameters
    │       └─ Create config for team consistency
    │
    └─ Need AUTOMATION/CI-CD?
        └─ YES → Use CLI
            └─ npx repomix --compress --style xml
            └─ Integrate in GitHub Actions/scripts
```

### Level 1: Slash Commands (90% of Tasks)

**Characteristics:**

- Highest abstraction level
- Natural language interface
- Context-aware and intelligent
- Minimal token consumption
- Automated workflow management

**Typical Users:**

- Developers exploring unfamiliar codebases
- Code reviewers analyzing pull requests
- Architects understanding system design
- Junior developers learning patterns
- Security engineers investigating vulnerabilities

**Example Scenarios:**

1. **New team member onboarding**
   ```
   /repomix-explorer:explore-local What is the architecture and how do services communicate?
   ```

2. **Bug investigation**
   ```
   /repomix-explorer:explore-local Find all error handling related to database connections
   ```

3. **Pattern identification**
   ```
   /repomix-explorer:explore-local What authentication patterns are used across the API?
   ```

4. **Quick documentation**
   ```
   /repomix-commands:pack-local Pack as markdown for sharing with team
   ```

**Advantages:**

- Zero cognitive overhead (no syntax to remember)
- AI agent handles complexity
- Optimal token usage
- Comprehensive results

**Limitations:**

- Less control over exact parameters
- Cannot customize packing pipeline
- Not suitable for automation
- Requires Claude Code environment

---

### Level 2: MCP Tools (10% Custom Workflows)

**Characteristics:**

- Programmatic control
- Precise parameter specification
- Configuration file integration
- Repeatable workflows
- Combinable operations

**Typical Users:**

- Teams needing standardized packing
- Developers building custom analysis tools
- Projects with specific filtering requirements
- Users requiring exact control over compression/formats

**Example Scenarios:**

1. **Team-standardized packing**
   ```typescript
   // Uses .repomixrc.json for consistency
   const result = await mcp__repomix__pack_codebase({
     directory: '/path/to/project',
     style: 'xml',
     compress: true
   });
   ```

2. **Multi-stage analysis**
   ```typescript
   // Pack once, analyze multiple times
   const packed = await mcp__repomix__pack_codebase({...});
   const security = await mcp__repomix__grep_repomix_output({
     outputId: packed.outputId,
     pattern: 'password|secret|token',
     contextLines: 5
   });
   const todos = await mcp__repomix__grep_repomix_output({
     outputId: packed.outputId,
     pattern: 'TODO|FIXME',
     contextLines: 3
   });
   ```

3. **Custom filtering workflow**
   ```typescript
   // Precise include/ignore patterns
   const result = await mcp__repomix__pack_codebase({
     directory: '/project',
     includePatterns: 'src/**/*.{ts,tsx}',
     ignorePatterns: '**/*.test.ts,**/*.spec.ts'
   });
   ```

**Advantages:**

- Exact parameter control
- Repeatable with configuration files
- Multi-stage workflows
- Programmatic integration

**Limitations:**

- Requires knowledge of parameters
- More verbose than slash commands
- Manual workflow management
- Token management is user responsibility

---

### Level 3: CLI (Automation Only)

**Characteristics:**

- Direct command-line interface
- Scriptable and automatable
- CI/CD integration ready
- No dependency on Claude Code
- Full control over all options

**Typical Users:**

- DevOps engineers
- CI/CD pipeline developers
- Automation script authors
- Users without Claude Code access

**Example Scenarios:**

1. **GitHub Actions integration**
   ```yaml
   - name: Pack codebase for analysis
     run: npx repomix --compress --style json -o packed.json
   ```

2. **Pre-commit hook**
   ```bash
   #!/bin/bash
   npx repomix --include-diffs --style markdown -o review.md
   ```

3. **Scheduled analysis**
   ```bash
   # Cron job: daily codebase snapshot
   0 0 * * * cd /project && npx repomix --compress -o daily-$(date +%Y%m%d).xml
   ```

**Advantages:**

- No Claude Code dependency
- Scriptable and automatable
- CI/CD integration
- Fastest for batch operations

**Limitations:**

- No intelligent analysis
- No context management
- Requires CLI syntax knowledge
- Manual result processing

---

## Performance Characteristics

### Token Efficiency Comparison

| Approach | Initial Overhead | Analysis Tokens | Total Efficiency |
|----------|------------------|-----------------|------------------|
| `/repomix-explorer:explore-local` | Low (agent packs) | Minimal (targeted search) | ⭐⭐⭐⭐⭐ Highest |
| `/repomix-commands:pack-local` | Medium (interpretation) | Full output | ⭐⭐⭐ Moderate |
| MCP Tools | Low (direct) | User-controlled | ⭐⭐⭐⭐ High |
| CLI | None | N/A (no LLM) | N/A |

**Key Insights:**

- **explore-local** uses least tokens: Agent retrieves only relevant sections
- **pack-local** generates full output: More tokens but still efficient
- **MCP tools** depend on user strategy: Can be highly efficient with grep
- **CLI** not applicable: No LLM involvement

### Context Management

**explore-local:**
- ✅ Automatic context management
- ✅ Prevents token exhaustion
- ✅ Intelligent search prioritization
- ✅ Focused result delivery

**pack-local:**
- ⚠️ Generates full packed output
- ⚠️ Can exceed context window for large repos
- ✅ User can follow up with grep
- ⚠️ Manual context management required

**MCP Tools:**
- ⚠️ User responsible for context management
- ✅ Incremental reading with line ranges
- ✅ grep_repomix_output for targeted search
- ✅ Attach pre-packed outputs

**CLI:**
- N/A (No LLM context)

### Speed Comparison

| Approach | Setup Time | Execution Time | Interactive? |
|----------|------------|----------------|--------------|
| explore-local | Instant | 3-7 seconds | Yes |
| pack-local | Instant | 2-5 seconds | Yes |
| MCP Tools | Config once | 2-5 seconds | Yes |
| CLI | None | 1-3 seconds | No |

**Key Insights:**

- **CLI fastest** for raw packing (no interpretation)
- **Slash commands** add ~1-2 seconds for intelligence
- **MCP tools** similar speed to CLI when configured
- **explore-local** takes longer but delivers analyzed results

---

## Integration Patterns

### Pattern 1: Configuration-Driven Workflow

**.repomixrc.json** provides foundation for all approaches:

```json
{
  "output": {
    "filePath": "repomix-output.xml",
    "style": "xml",
    "compress": false
  },
  "ignore": {
    "useGitignore": true,
    "customPatterns": [
      "**/bin/**",
      "**/obj/**",
      "**/*.test.ts"
    ]
  }
}
```

**Usage:**

```bash
# Slash commands use config automatically
/repomix-commands:pack-local Pack this project

# MCP tools respect config
mcp__repomix__pack_codebase({ directory: '/project' })

# CLI uses config
npx repomix
```

**Benefit:** Team-wide consistency regardless of approach.

---

### Pattern 2: Workflow Escalation

Start high-level, drop down when needed:

```
STEP 1: Explore (slash command)
/repomix-explorer:explore-local Find authentication logic

    ↓ Need more detail?

STEP 2: Grep (MCP tool)
mcp__repomix__grep_repomix_output({
  pattern: 'authenticate|login',
  contextLines: 10
})

    ↓ Need full implementation?

STEP 3: Read (MCP tool)
mcp__repomix__read_repomix_output({
  startLine: 150,
  endLine: 250
})
```

**Benefit:** Progressive disclosure - use tokens only when needed.

---

### Pattern 3: Hybrid Analysis

Combine approaches for optimal workflow:

```typescript
// 1. Use slash command for initial exploration
/repomix-explorer:explore-local What security vulnerabilities might exist?

// 2. Pack with MCP tools for detailed analysis
const result = await mcp__repomix__pack_codebase({
  directory: '/project',
  compress: false,  // Need implementation details
  style: 'xml'
});

// 3. Multiple targeted searches
const sqlInjection = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'execute.*\\+|query.*\\+',
  contextLines: 5
});

const xss = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'innerHTML|dangerouslySetInnerHTML',
  contextLines: 3
});

// 4. Generate report
```

**Benefit:** Leverage strengths of each approach.

---

### Pattern 4: CI/CD Integration

Use CLI for automation, MCP tools for interactive review:

```yaml
# .github/workflows/code-analysis.yml
name: Code Analysis

on: [pull_request]

jobs:
  pack:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Pack codebase
        run: npx repomix --compress --style json -o packed.json

      - name: Upload artifact
        uses: actions/upload-artifact@v3
        with:
          name: packed-codebase
          path: packed.json
```

Then in Claude Code:

```typescript
// Download and attach artifact
await mcp__repomix__attach_packed_output({
  path: '/downloads/packed.json'
});

// Analyze interactively
```

**Benefit:** Automation + interactive analysis.

---

## Real-World Scenarios

### Scenario 1: New Team Member Onboarding

**Goal:** Understand unfamiliar codebase quickly

**Optimal Approach:** Level 1 (explore-local)

```bash
# Day 1: Overall architecture
/repomix-explorer:explore-local What is the overall architecture and component structure?

# Day 2: Specific domain
/repomix-explorer:explore-local How does the payment processing system work?

# Day 3: Patterns and conventions
/repomix-explorer:explore-local What testing patterns and coding conventions are used?

# Day 4: Integration points
/repomix-explorer:explore-local How do external services integrate with the API?
```

**Why:** Natural language queries, minimal cognitive load, AI-guided learning.

---

### Scenario 2: Code Review Workflow

**Goal:** Review pull request changes comprehensively

**Optimal Approach:** Hybrid (slash command + MCP tools)

```bash
# 1. Initial exploration
/repomix-explorer:explore-local What changed in this PR and what might be the impact?

# 2. Detailed analysis (if needed)
const result = await mcp__repomix__pack_codebase({
  directory: '/project',
  compress: false
});

# 3. Search for specific concerns
const todos = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'TODO|FIXME|HACK',
  contextLines: 3
});

const debugging = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'console\\.log|debugger',
  contextLines: 2
});
```

**Why:** Quick overview (slash command), detailed analysis when needed (MCP tools).

---

### Scenario 3: Security Audit

**Goal:** Comprehensive security vulnerability assessment

**Optimal Approach:** Level 2 (MCP tools)

```typescript
// Pack without compression (need implementation details)
const result = await mcp__repomix__pack_codebase({
  directory: '/project',
  compress: false,
  style: 'xml'
});

// Search for vulnerability patterns
const patterns = [
  { name: 'SQL Injection', regex: 'execute.*\\+|query.*\\+' },
  { name: 'XSS', regex: 'innerHTML|dangerouslySetInnerHTML' },
  { name: 'Hardcoded Secrets', regex: 'password\\s*=\\s*["\']|api_key\\s*=' },
  { name: 'Weak Crypto', regex: 'md5|sha1(?!\\.)' },
  { name: 'Path Traversal', regex: '\\.\\.\\/|path\\.join.*req\\.' }
];

for (const pattern of patterns) {
  const findings = await mcp__repomix__grep_repomix_output({
    outputId: result.outputId,
    pattern: pattern.regex,
    contextLines: 5
  });
  // Analyze findings
}
```

**Why:** Need precise control, multiple searches, implementation-level detail.

---

### Scenario 4: Documentation Generation

**Goal:** Generate API documentation from codebase

**Optimal Approach:** Level 2 (MCP tools with compression)

```typescript
// Pack with compression (signatures sufficient)
const result = await mcp__repomix__pack_codebase({
  directory: '/project',
  compress: true,  // Only need signatures
  style: 'markdown',
  includePatterns: 'src/**/*.ts'
});

// Extract public API
const exports = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'export\\s+(function|class|interface|type)',
  contextLines: 10
});

// Generate documentation from signatures
```

**Why:** Compression ideal for docs, need structured extraction.

---

### Scenario 5: CI/CD Automated Analysis

**Goal:** Pack codebase in GitHub Actions for later analysis

**Optimal Approach:** Level 3 (CLI) + Level 2 (MCP tools)

```yaml
# GitHub Actions workflow
- name: Pack codebase
  run: npx repomix --compress --style json -o packed.json

- name: Upload artifact
  uses: actions/upload-artifact@v3
  with:
    name: packed-codebase
    path: packed.json
```

Then interactively:

```typescript
// Attach and analyze
await mcp__repomix__attach_packed_output({
  path: '/downloads/packed.json'
});

// Interactive exploration
```

**Why:** CLI for automation, MCP tools for interactive analysis.

---

### Scenario 6: Refactoring Planning

**Goal:** Identify code duplication and complexity hotspots

**Optimal Approach:** Hybrid (explore-local + MCP tools)

```bash
# 1. Initial exploration
/repomix-explorer:explore-local Find duplicated code patterns and complexity hotspots

# 2. Detailed analysis if needed
const result = await mcp__repomix__pack_codebase({
  directory: '/project',
  compress: true  // Structure analysis only
});

# 3. Find specific patterns
const duplication = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'function\\s+validate\\w+',  // Similar function names
  contextLines: 5
});
```

**Why:** AI identifies patterns (explore-local), manual verification (MCP tools).

---

### Scenario 7: Migration Planning

**Goal:** Understand impact of migrating from Redux to Zustand

**Optimal Approach:** Level 1 (explore-local)

```bash
# Identify all Redux usage
/repomix-explorer:explore-local Find all Redux usage including stores, reducers, actions, and selectors

# Identify state patterns
/repomix-explorer:explore-local What state management patterns are used and where?

# Identify dependencies
/repomix-explorer:explore-local What components depend on Redux state?
```

**Why:** Natural language queries perfect for exploratory analysis.

---

### Scenario 8: Performance Optimization

**Goal:** Identify performance bottlenecks in React application

**Optimal Approach:** Level 2 (MCP tools uncompressed)

```typescript
// Need implementation details (no compression)
const result = await mcp__repomix__pack_codebase({
  directory: '/project',
  compress: false,
  includePatterns: 'src/**/*.{tsx,ts}'
});

// Find performance anti-patterns
const renders = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'useEffect\\(|map\\(.*=>|filter\\(.*=>',
  contextLines: 10
});

// Analyze implementation details
```

**Why:** Need full implementation to identify subtle performance issues.

---

### Scenario 9: Test Coverage Analysis

**Goal:** Identify untested code areas

**Optimal Approach:** Hybrid

```bash
# 1. Overview
/repomix-explorer:explore-local What is the current test coverage and patterns?

# 2. Find untested files
const src = await mcp__repomix__pack_codebase({
  directory: '/project/src',
  includePatterns: '**/*.ts'
});

const tests = await mcp__repomix__pack_codebase({
  directory: '/project/test',
  includePatterns: '**/*.test.ts'
});

# 3. Compare and identify gaps
```

**Why:** Overview (slash command), detailed comparison (MCP tools).

---

### Scenario 10: Learning External Library

**Goal:** Understand how a library works internally

**Optimal Approach:** Level 1 (explore-local with remote)

```bash
# For remote repositories
/repomix-explorer:explore-local Explain the architecture of facebook/react hooks implementation

# For local clone
/repomix-explorer:explore-local How does useState work internally?
```

**Why:** AI-guided learning, natural language questions, context-efficient.

---

## Best Practices

### When to Use Each Level

**Use Level 1 (Slash Commands) When:**

- ✅ Exploring unfamiliar codebase
- ✅ Asking "how does X work?"
- ✅ Finding specific patterns or logic
- ✅ Quick one-off packing
- ✅ Learning and understanding
- ✅ Initial orientation
- ✅ Natural language queries preferred

**Use Level 2 (MCP Tools) When:**

- ✅ Need exact parameter control
- ✅ Building custom workflows
- ✅ Repeated analysis with same settings
- ✅ Multi-stage analysis (pack once, grep many)
- ✅ Team standardization required
- ✅ Precise filtering needed
- ✅ Combining multiple operations

**Use Level 3 (CLI) When:**

- ✅ CI/CD integration
- ✅ Automation scripts
- ✅ GitHub Actions / pre-commit hooks
- ✅ No Claude Code available
- ✅ Batch processing
- ✅ Scheduled tasks

### Configuration Strategy

**Always create `.repomixrc.json`:**

- Provides defaults for all approaches
- Ensures team consistency
- Documents project-specific ignore patterns
- Simplifies repeated operations

**Example:**

```json
{
  "output": {
    "style": "xml",
    "compress": false
  },
  "ignore": {
    "useGitignore": true,
    "customPatterns": [
      "**/test/**",
      "**/*.test.ts"
    ]
  }
}
```

### Token Optimization

1. **Start with explore-local** - Most token-efficient
2. **Use compression** when structure analysis sufficient
3. **Filter aggressively** with includePatterns
4. **Grep before reading** entire output
5. **Read line ranges** instead of full files

### Workflow Escalation

```
explore-local (quick understanding)
    ↓ Need more control?
pack-local (quick packing)
    ↓ Need precise parameters?
MCP tools (custom workflow)
    ↓ Need automation?
CLI (scripts/CI-CD)
```

---

## Summary

### Quick Reference

| Need | Use | Command |
|------|-----|---------|
| Understand codebase | explore-local | `/repomix-explorer:explore-local Find auth logic` |
| Quick pack | pack-local | `/repomix-commands:pack-local Pack as markdown` |
| Custom workflow | MCP tools | `mcp__repomix__pack_codebase(...)` |
| Automation | CLI | `npx repomix --compress` |

### Key Takeaways

1. **90% of tasks** → Use slash commands
2. **10% custom workflows** → Use MCP tools
3. **Automation only** → Use CLI
4. **.repomixrc.json** → Foundation for all approaches
5. **Start high-level** → Drop down only when needed

### Performance Hierarchy

```
Token Efficiency:    explore-local > MCP tools > pack-local > CLI (N/A)
Context Management:  explore-local > MCP tools > pack-local > CLI (N/A)
Control Level:       CLI > MCP tools > pack-local > explore-local
Automation Support:  CLI > MCP tools > pack-local > explore-local
Learning Curve:      explore-local < pack-local < MCP tools < CLI
```

### Optimization Mantra

**"Use the highest-level tool that meets your needs."**

- High-level tools provide intelligence and efficiency
- Low-level tools provide control and automation
- All tools work together through shared configuration
- Escalate only when necessary
