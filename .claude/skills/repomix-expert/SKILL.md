---
name: repomix-expert
description: Pack and analyze entire codebases when holistic understanding is needed that individual file reads cannot provide. Activates for architecture exploration, cross-file pattern analysis, unfamiliar codebase navigation, efficient context consolidation, remote repository investigation, code review preparation, or when fragmented searches aren't yielding results. Also generates reusable Claude Agent Skills from any codebase for permanent, zero-cost reference. Covers /repomix-explorer, /repomix-commands slash commands, MCP tools, CLI, and --skill-generate. Triggers on "repomix", "pack repository", "analyze whole codebase", "understand this project", "explore architecture", "generate skill", "create codebase reference", "skill-generate".
---

# Repomix Expert Skill

Comprehensive expertise in Repomix, a tool that packs entire repositories into single, AI-friendly files optimized for analysis by Large Language Models. Also generates reusable Claude Agent Skills for permanent codebase references.

## Purpose

Provide complete mastery of Repomix for:
- Packing local and remote repositories
- **Generating Claude Agent Skills** for permanent, zero-cost codebase references
- Analyzing codebases efficiently
- Optimizing token usage with compression
- Security scanning and sensitive data protection
- Code review, documentation, and refactoring workflows
- Integration with Claude Code via slash commands and MCP tools

## Optimization Hierarchy

Repomix provides three interaction levels. **Always start with the highest level that meets your needs:**

```
┌─────────────────────────────────────────────────────────────┐
│ LEVEL 1: Claude Code Slash Commands (RECOMMENDED - 90%)     │
│ ├─ /repomix-explorer:explore-local - AI-driven analysis     │
│ ├─ /repomix-explorer:explore-remote - Remote GitHub repos   │
│ ├─ /repomix-commands:pack-local - Natural language packing  │
│ └─ Use for: Interactive exploration and quick packing       │
├─────────────────────────────────────────────────────────────┤
│ LEVEL 2: MCP Tools (Custom Workflows - 10%)                 │
│ ├─ mcp__repomix__pack_codebase - Programmatic control       │
│ ├─ mcp__repomix__grep_repomix_output - Targeted search      │
│ └─ Use for: Precise control, repeated analysis, team std    │
├─────────────────────────────────────────────────────────────┤
│ LEVEL 3: CLI (Automation Only)                              │
│ ├─ npx repomix --options - Direct command-line              │
│ └─ Use for: CI/CD pipelines, automation scripts             │
└─────────────────────────────────────────────────────────────┘
```

**Complete optimization guide in `references/claude_code_plugins.md`**

## Agent Skills Generation (Primary Use Case)

**Generate permanent, reusable codebase references instead of runtime packing.**

Complete guide in `references/agent_skills_generation.md`.

```bash
# Generate skill from current directory
repomix --skill-generate

# Generate with custom name
repomix --skill-generate my-project-reference

# Generate from specific directory
repomix src/ --skill-generate

# Generate from remote repository
repomix --remote user/repo --skill-generate

# Generate with compression (recommended for large codebases)
repomix --skill-generate --compress
```

**Why use Agent Skills Generation?**

| Approach | Token Cost | Speed | Persistence |
|----------|------------|-------|-------------|
| Runtime packing | High (every use) | 2-5 seconds | None |
| Generated Skill | Zero (pre-loaded) | Instant | Permanent |

**Generated Structure:**

```
.claude/skills/<skill-name>/
├── SKILL.md                    # Metadata & statistics
└── references/
    ├── summary.md              # Purpose, format, statistics
    ├── project-structure.md    # Directory tree with line counts
    ├── files.md                # All file contents (grep-friendly)
    └── tech-stack.md           # Auto-detected languages & frameworks
```

**Storage Locations:**
- `~/.claude/skills/` - Personal (available across all projects)
- `.claude/skills/` - Project (shared via git with team)

**Primary Use Cases:**
1. **Personal reference library** - Generate skills from frameworks/libraries you use
2. **Team documentation** - Commit project skills to git for team access
3. **Learning resources** - Generate skills from well-structured open source projects
4. **Dependency investigation** - Understand how libraries work internally

**Recommendation:** For stable codebases and external references, **always generate a Skill** instead of runtime packing. This is the most efficient way to give Claude persistent codebase knowledge.

## Key Capabilities

This skill enables:
- **Agent Skills Generation** - Create permanent, zero-cost codebase references for Claude
- **Holistic codebase analysis** - Understand architecture when file-by-file reads are insufficient
- **Cross-file pattern detection** - Find how patterns propagate across the entire codebase
- **Efficient context consolidation** - Pack large codebases into optimized, searchable outputs
- **Remote repository investigation** - Analyze GitHub repos without cloning
- **Token optimization** - ~70% reduction via Tree-sitter compression
- **Workflow automation** - Code review, security audit, documentation generation

## Tools Reference

### 1. CLI Usage

Complete command-line interface reference available in `references/cli_reference.md`.

**Quick Reference:**

```bash
# Pack current directory
repomix

# Pack with compression (~70% token reduction)
repomix --compress

# Pack remote repository
repomix --remote user/repo

# Pack with filtering
repomix --include "src/**/*.ts" --ignore "**/*.test.ts"

# Pack for code review
repomix --include-diffs --include-logs --style markdown

# Split large outputs (for AI tools with size limits)
repomix --split-output 1mb

# Generate Claude Agent Skills package
repomix --skill-generate my-project-reference
```

**Search Pattern:**
When user needs specific CLI options, search `references/cli_reference.md` for keywords like "compress", "remote", "git", "security", "token", "style", etc.

### 2. Configuration

Complete configuration guide in `references/configuration_guide.md`.

**Configuration File Priority:**
1. `repomix.config.ts` (TypeScript - RECOMMENDED)
2. `repomix.config.js` (JavaScript)
3. `repomix.config.json` (JSON)

**Template Configurations:**

Use pre-built templates from `assets/config-templates/`:
- `basic.json` - Minimal configuration to start
- `compressed.json` - Token-optimized with compression
- `security-focused.json` - Security audit configuration
- `git-integrated.json` - Code review with git context
- `dotnet.json` - .NET project optimized

**Search Pattern:**
For configuration questions, search `references/configuration_guide.md` for section names or option names.

### 3. MCP Tools Integration

Complete MCP tools reference in `references/mcp_tools.md`.

**Available MCP Tools:**

```typescript
// Pack local directory
await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'xml',
  compress: true
});

// Pack remote repository
await mcp__repomix__pack_remote_repository({
  remote: 'user/repo',
  style: 'json',
  compress: true
});

// Search packed output
await mcp__repomix__grep_repomix_output({
  outputId: 'result_id',
  pattern: 'function\\s+\\w+',
  contextLines: 5
});

// Read specific range
await mcp__repomix__read_repomix_output({
  outputId: 'result_id',
  startLine: 100,
  endLine: 200
});

// Attach existing output
await mcp__repomix__attach_packed_output({
  path: '/path/to/repomix-output.xml'
});
```

**Search Pattern:**
For MCP tool details, search `references/mcp_tools.md` for tool names or workflow patterns.

### 4. Workflows

Common workflows and use cases in `references/workflows.md`.

**Available Workflows:**
- Code Review (pre-commit, PR review, remote review)
- Documentation Generation (API docs, architecture docs, changelog)
- Security Audit (vulnerability scan, dependency audit)
- Refactoring (dead code detection, duplication, complexity analysis)
- Learning and Exploration (understand new codebase, pattern mining)
- Migration Planning (dependency migration, framework migration)
- Performance Optimization (bundle size analysis, hotspot detection)
- Test Coverage Analysis
- Compliance and Standards

**Search Pattern:**
For workflow guidance, search `references/workflows.md` for use case keywords like "code review", "security", "documentation", "refactoring", etc.

### 5. Tree-sitter Compression

Complete compression guide in `references/compression_guide.md`.

**Compression Benefits:**
- ~70% token reduction
- Preserves: imports, exports, signatures, types, interfaces, comments
- Removes: function bodies, implementation details

**When to Compress:**
- Large codebases
- Architecture analysis
- API documentation
- High-level overview
- Type checking
- Initial exploration

**When NOT to Compress:**
- Bug investigation
- Performance optimization
- Security audit
- Detailed code review
- Test writing
- Migration execution

**Search Pattern:**
For compression questions, search `references/compression_guide.md` for "when to use", "language support", "strategies", etc.

### 6. Output Formats

Complete format guide in `references/output_formats.md`.

**Available Formats:**
- **XML** (default) - Best for AI analysis, structured parsing
- **Markdown** - Best for human reading, documentation
- **JSON** - Best for programmatic processing
- **Plain** - Best for simple tools, grep

**Format Selection:**
- Choose XML for AI analysis and structured data
- Choose Markdown for documentation and readability
- Choose JSON for automated pipelines and parsing
- Choose Plain only when necessary

**Search Pattern:**
For format questions, search `references/output_formats.md` for format names or use cases.

### 7. Claude Code Plugin Integration

Complete slash command reference in `references/claude_code_plugins.md`.

**Available Slash Commands:**

#### /repomix-explorer:explore-local

AI-driven codebase exploration with natural language queries:

```bash
# Find specific patterns or logic
/repomix-explorer:explore-local Find all authentication logic in the codebase

# Understand architecture
/repomix-explorer:explore-local How does error handling work across the application?

# Identify patterns
/repomix-explorer:explore-local What testing patterns are used?
```

**Advantages:**
- Context-efficient (agent retrieves only relevant sections)
- Intelligent search (understands intent, searches multiple patterns)
- Automated workflow (no manual pack → grep → read)
- Prevents token exhaustion

**Use When:**
- Exploring unfamiliar codebase (most common use case)
- Finding specific patterns or logic
- Understanding architecture
- Learning how systems work

#### /repomix-explorer:explore-remote

Explore remote GitHub repositories without cloning:

```bash
# Analyze a popular library
/repomix-explorer:explore-remote facebook/react How do hooks work internally?

# Understand a framework's patterns
/repomix-explorer:explore-remote vercel/next.js What routing patterns are used?
```

**Use When:**
- Learning from open source projects
- Investigating external library internals
- Due diligence on dependencies

#### /repomix-commands:pack-local

Natural language interface for packing:

```bash
# Pack with natural language
/repomix-commands:pack-local Pack this project as XML with compression enabled

# Simple directives
/repomix-commands:pack-local Pack as markdown for documentation

# Filtering with natural language
/repomix-commands:pack-local Pack TypeScript files only, exclude tests
```

**Advantages:**
- No CLI syntax required
- Intent interpretation by AI
- Uses `repomix.config.json` defaults
- Quick one-off packing

**Use When:**
- Quick packing without complex requirements
- Don't remember exact CLI syntax
- Experimenting with formats
- Ad-hoc analysis

**Integration:**

Slash commands leverage MCP tools internally and respect `repomix.config.json` configuration:

```
Slash Command → Interprets intent → Calls MCP tools → Uses repomix.config.json
```

**Optimization Strategy:**

1. **Start with slash commands** (90% of tasks)
2. **Drop to MCP tools** when need precise control (10% of tasks)
3. **Use CLI** only for automation/CI-CD

**Complete guide:** `references/claude_code_plugins.md` includes:
- Detailed slash command reference
- Decision tree for approach selection
- Performance characteristics
- 10+ real-world scenarios
- Integration patterns
- Best practices

**Search Pattern:**
For slash command optimization questions, search `references/claude_code_plugins.md` for "decision tree", "optimization", "scenario", or specific use case keywords.

## Utility Scripts

### 1. Configuration Validator

Script: `scripts/validate_config.py`

Validate `repomix.config.json` files for syntax and semantic correctness.

```bash
# Validate config in current directory
python3 scripts/validate_config.py

# Validate specific config
python3 scripts/validate_config.py path/to/repomix.config.json
```

**Use When:**
- Creating new configuration
- Debugging configuration errors
- Ensuring configuration validity

### 2. Token Analyzer

Script: `scripts/token_analyzer.py`

Analyze token distribution in packed outputs.

```bash
# Analyze XML output
python3 scripts/token_analyzer.py repomix-output.xml

# Analyze JSON output
python3 scripts/token_analyzer.py packed-repo.json
```

**Provides:**
- Top files by token count
- Token distribution by directory
- Token distribution by file extension
- Optimization recommendations
- Context window usage estimates

**Use When:**
- Packed output too large for context
- Need to optimize token usage
- Want to identify largest files
- Planning compression strategy

### 3. Quick Pack

Script: `scripts/quick_pack.sh`

Preset packing workflows for common use cases.

```bash
# Compressed overview
./scripts/quick_pack.sh overview

# Code review with git context
./scripts/quick_pack.sh review /path/to/project

# TypeScript project
./scripts/quick_pack.sh typescript ./src

# Security audit
./scripts/quick_pack.sh security
```

**Presets:**
- `overview` - Compressed with token tree
- `review` - With diffs and logs
- `docs` - Documentation optimized
- `security` - Security audit
- `full` - Complete uncompressed
- `typescript` - TS/TSX optimized
- `python` - Python optimized
- `dotnet` - .NET optimized
- `web` - Web project optimized

**Use When:**
- Need quick, preset workflows
- Don't want to remember all options
- Want language-specific optimization

## Best Practices

### Token Optimization

1. **Always start compressed** for large codebases
2. **Filter aggressively** with `--include` patterns
3. **Use token analyzer** to identify optimization opportunities
4. **Remove comments** if documentation not needed: `--remove-comments`
5. **Progressive disclosure**: Compress first, expand as needed

### Security

1. **Never disable security scanning** (default enabled)
2. **Review .repomixignore** for sensitive file patterns
3. **Use security-focused template** for audits
4. **Verify output** before sharing externally

### Workflows

1. **Use MCP tools** for interactive analysis
2. **Use CLI** for automation and CI/CD
3. **Combine tools**: pack → grep → analyze
4. **Attach pre-packed outputs** for repeated analysis
5. **Leverage git integration** for code reviews

### Performance

1. **Compression adds ~2x processing time** but reduces tokens by 70%
2. **Filter early** with include/ignore patterns
3. **Pack remote repos directly** instead of cloning first
4. **Use JSON format** for smallest file size
5. **Check token count tree** before full analysis

## Common Patterns

### Pattern 1: Code Review Workflow

```typescript
// 1. Pack with git context
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'markdown',
  compress: false
});

// 2. Search for specific patterns
const patterns = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'TODO|FIXME|HACK',
  ignoreCase: true,
  contextLines: 3
});

// 3. Analyze and provide review
```

### Pattern 2: Security Audit Workflow

```typescript
// 1. Pack without compression (need implementation)
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'xml'
});

// 2. Search for vulnerabilities
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

// 3. Report findings
```

### Pattern 3: Documentation Generation

```typescript
// 1. Pack with compression
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'markdown',
  compress: true,
  includePatterns: 'src/**,**/*.md'
});

// 2. Extract public API
const api = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'export\\s+(function|class|interface|type)',
  contextLines: 10
});

// 3. Generate documentation
```

### Pattern 4: Remote Repository Analysis

```typescript
// 1. Pack remote repository
const result = await mcp__repomix__pack_remote_repository({
  remote: 'facebook/react',
  compress: true,
  includePatterns: 'packages/**/*.ts'
});

// 2. Search for patterns
const hooks = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'function\\s+use\\w+',
  contextLines: 5
});

// 3. Analyze architecture
```

## Troubleshooting

### Output Too Large

**Problem:** Packed output exceeds context window

**Solutions:**
1. Enable compression: `--compress`
2. Filter files: `--include "src/**/*.ts"`
3. Use token analyzer to identify large files
4. Remove comments: `--remove-comments`
5. Analyze incrementally with grep

### Security Check Failures

**Problem:** Repomix detects sensitive data

**Solutions:**
1. Review reported files
2. Add patterns to `.repomixignore`
3. Verify files don't contain actual secrets
4. Fix security issues before proceeding

### Parser Errors

**Problem:** Tree-sitter compression fails

**Solutions:**
1. Files fall back to full content automatically
2. Check for syntax errors in source
3. Verify language support
4. Update Repomix to latest version

### Missing Files

**Problem:** Expected files not in output

**Solutions:**
1. Check `.gitignore` patterns
2. Verify custom ignore patterns
3. Use `--verbose` to see processing
4. Check `--include` patterns

## Reference Documentation

All comprehensive references available in `references/` directory:

1. **agent_skills_generation.md** - Generate permanent Claude Skills from codebases (RECOMMENDED)
2. **claude_code_plugins.md** - Slash commands, optimization hierarchy, decision tree
3. **cli_reference.md** - Complete CLI options (40+ flags)
4. **configuration_guide.md** - Configuration file reference
5. **mcp_tools.md** - MCP tool documentation
6. **workflows.md** - Common use case workflows
7. **compression_guide.md** - Tree-sitter compression details
8. **output_formats.md** - Format comparison and selection

**Large files note:** Reference files are comprehensive. Use grep/search to find specific topics rather than reading entire files.

**Search Patterns:**
```bash
# Find CLI option
grep -i "compress" references/cli_reference.md

# Find configuration option
grep -i "tokenCount" references/configuration_guide.md

# Find workflow
grep -i "security audit" references/workflows.md
```

### Configuration Templates

Located in `assets/config-templates/`:
- `basic.json` - Minimal starting point
- `compressed.json` - Token-optimized
- `security-focused.json` - Security audit
- `git-integrated.json` - Code review
- `dotnet.json` - .NET projects

### Ignore Patterns

Example `.repomixignore` in `assets/example.repomixignore`

Contains common patterns for:
- Dependencies (node_modules, vendor)
- Build outputs (dist, build)
- IDE files
- Test coverage
- Generated files

## Integration with Claude Code

### MCP Server Setup

Ensure MCP server configured in `.mcp.json`:

```json
{
  "mcpServers": {
    "repomix": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "repomix", "--mcp"]
    }
  }
}
```

And permissions in `.claude/settings.json`:

```json
{
  "permissions": {
    "allow": [
      "mcp__repomix"
    ]
  }
}
```

### Workflow Integration

1. Use MCP tools for interactive analysis
2. Pack once, analyze multiple times with grep
3. Combine with other skills (code review, documentation)
4. Leverage compression for large codebases

## Skill Mastery Checklist

To become Repomix expert, understand:

- [x] **Agent Skills Generation** - Create permanent codebase references
- [x] CLI options and flags
- [x] Configuration file structure
- [x] MCP tool capabilities
- [x] Tree-sitter compression mechanics
- [x] Output format selection
- [x] Common workflows
- [x] Security scanning
- [x] Token optimization strategies
- [x] Integration patterns
- [x] Troubleshooting approaches

## Summary

Repomix Expert skill provides complete mastery of:
- **Agent Skills Generation** - Create permanent, zero-cost Claude references from any codebase
- Packing repositories (local and remote)
- Token optimization with compression
- Security scanning and data protection
- Multiple output formats
- Common workflows (review, docs, security, refactoring)
- MCP tool integration with Claude Code
- Utility scripts for validation and analysis

**Key recommendation:** For stable codebases and external references, **generate a Skill** (`--skill-generate`) instead of runtime packing. This is the most token-efficient way to give Claude persistent codebase knowledge.
