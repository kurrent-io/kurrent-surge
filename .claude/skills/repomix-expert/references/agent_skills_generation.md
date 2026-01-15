# Agent Skills Generation

Generate reusable Claude Agent Skills from any codebase. Creates structured reference directories that Claude can access instantly without runtime packing.

## Why Use Agent Skills Generation?

| Approach | Token Cost | Speed | Persistence |
|----------|------------|-------|-------------|
| Runtime `repomix` packing | High (every use) | 2-5 seconds | None |
| Pre-generated Skill | Zero (pre-loaded) | Instant | Permanent |

**Key insight:** Instead of packing codebases at runtime, generate a Skill once and reference it forever. This is the most efficient way to give Claude persistent codebase knowledge.

## Quick Start

```bash
# Generate from current directory
repomix --skill-generate

# Generate with custom name
repomix --skill-generate my-project-reference

# Generate from specific directory
repomix src/ --skill-generate

# Generate from remote repository
repomix --remote user/repo --skill-generate
repomix --remote https://github.com/facebook/react --skill-generate
```

## Generated Structure

```
.claude/skills/<skill-name>/
├── SKILL.md                    # Metadata, statistics, usage docs
└── references/
    ├── summary.md              # Purpose, format, file breakdown
    ├── project-structure.md    # Directory tree with line counts
    ├── files.md                # All file contents (grep-friendly)
    └── tech-stack.md           # Auto-detected languages & frameworks
```

### File Purposes

| File | Purpose | Use For |
|------|---------|---------|
| `SKILL.md` | Entry point with metadata and token counts | Understanding skill scope |
| `summary.md` | Statistics by file type, usage guidelines | Planning analysis approach |
| `project-structure.md` | Hierarchical tree with line counts | File discovery and navigation |
| `files.md` | Complete source code with syntax headers | Grep-friendly code search |
| `tech-stack.md` | Languages, frameworks, dependencies | Technology understanding |

## Storage Locations

Choose where to store generated Skills:

### Personal Skills (`~/.claude/skills/`)

- Available across **all projects** on your machine
- Not shared with team
- Best for: personal reference libraries, learning resources

```bash
# Generate to personal skills
repomix --remote vercel/next.js --skill-generate
# → ~/.claude/skills/repomix-reference-next.js/
```

### Project Skills (`.claude/skills/`)

- Shared via git with team
- Project-specific
- Best for: team documentation, shared references

```bash
# Generate to project skills (run in project root)
repomix src/ --skill-generate
# → .claude/skills/repomix-reference-src/
```

## Auto-Naming Convention

When no custom name provided, Repomix generates names automatically:

| Command | Generated Name |
|---------|----------------|
| `repomix --skill-generate` | `repomix-reference-<dirname>` |
| `repomix src/ --skill-generate` | `repomix-reference-src` |
| `repomix --remote user/repo --skill-generate` | `repomix-reference-repo` |

Custom names are normalized:
- Converted to kebab-case
- Maximum 64 characters
- Special characters removed

## Advanced Options

### Filtering Content

```bash
# Only TypeScript files
repomix --skill-generate --include "src/**/*.ts"

# Exclude tests
repomix --skill-generate --ignore "**/*.test.ts,**/*.spec.ts"

# Combine filters
repomix --skill-generate --include "src/**" --ignore "**/node_modules/**"
```

### Compression

```bash
# Compress for smaller token footprint (~70% reduction)
repomix --skill-generate --compress

# Remove comments and empty lines
repomix --skill-generate --remove-comments --remove-empty-lines
```

### Documentation-Only Skills

Create Skills from documentation directories only:

```bash
# Reference a library's docs
repomix --remote anthropics/claude-code-action --include docs --skill-generate

# Reference multiple doc directories
repomix --skill-generate --include "docs/**,README.md,CONTRIBUTING.md"
```

## Use Cases

### 1. Personal Reference Library

Build a collection of Skills from projects you frequently reference:

```bash
# Create personal library of framework references
repomix --remote facebook/react --skill-generate
repomix --remote microsoft/TypeScript --skill-generate
repomix --remote tailwindlabs/tailwindcss --skill-generate

# Now Claude can instantly reference any of these
```

### 2. Team Project Documentation

Generate Skills for your project and commit to git:

```bash
# Generate project reference
repomix src/ --skill-generate project-core

# Add to git (or add to .gitignore if too large)
git add .claude/skills/project-core/
```

### 3. Learning New Codebases

Generate Skills from open source projects to learn patterns:

```bash
# Learn from well-structured projects
repomix --remote vercel/next.js --compress --skill-generate nextjs-patterns
repomix --remote prisma/prisma --compress --skill-generate prisma-patterns
```

### 4. Framework Documentation Reference

Create Skills from framework docs for coding guidance:

```bash
# ASP.NET Core documentation
repomix --remote dotnet/AspNetCore.Docs --include "aspnetcore/**/*.md" --skill-generate aspnet-docs

# React documentation
repomix --remote reactjs/react.dev --include "src/content/**" --skill-generate react-docs
```

### 5. Dependency Investigation

Understand how a dependency works internally:

```bash
# Investigate a library you depend on
repomix --remote grpc/grpc-dotnet --compress --skill-generate grpc-dotnet-internals
```

## Integration with Claude

### Automatic Loading

Skills in standard locations are automatically available:
- `~/.claude/skills/*` - Personal (always available)
- `.claude/skills/*` - Project (available in that project)

### Triggering

Claude loads Skills based on SKILL.md description triggers. Generated Skills include:
- Project name and purpose
- Technology stack
- Key patterns and structures

### Searching Generated Skills

The `files.md` reference is optimized for grep:

```bash
# Search for authentication patterns
grep -i "authentication" .claude/skills/my-project/references/files.md

# Find specific function
grep "async function" .claude/skills/my-project/references/files.md
```

## Best Practices

### 1. Use Compression for Large Projects

```bash
# Reduce token count by ~70%
repomix --skill-generate --compress
```

### 2. Filter Aggressively

```bash
# Only include what's needed
repomix --skill-generate \
  --include "src/**/*.ts" \
  --ignore "**/*.test.ts,**/*.spec.ts,**/node_modules/**"
```

### 3. Gitignore Large Skills

Add to `.gitignore` if Skills are too large to commit:

```gitignore
# Large generated skills
.claude/skills/repomix-reference-*/
```

### 4. Regenerate After Major Changes

Skills are snapshots. Regenerate after significant codebase changes:

```bash
# Remove old and regenerate
rm -rf .claude/skills/my-project/
repomix src/ --skill-generate my-project
```

### 5. Separate Concerns

Generate multiple focused Skills instead of one large one:

```bash
# Split by concern
repomix src/api/ --skill-generate project-api
repomix src/core/ --skill-generate project-core
repomix src/ui/ --skill-generate project-ui
```

## Incompatible Options

Cannot combine `--skill-generate` with:
- `--stdout` - Skills require filesystem output
- `--copy` - Directory output incompatible with clipboard

## Comparison: Skills vs Runtime Packing

| Feature | Generated Skill | Runtime Packing |
|---------|-----------------|-----------------|
| **Speed** | Instant | 2-5 seconds |
| **Token cost** | Pre-paid (generation time) | Every use |
| **Persistence** | Permanent until deleted | None |
| **Freshness** | Snapshot at generation | Always current |
| **Best for** | Stable references, learning | Active development |

**Recommendation:**
- Use **generated Skills** for: external libraries, documentation, stable code, learning
- Use **runtime packing** for: active development, frequently changing code

## Workflow: Setting Up Project Skills

Complete workflow for a new project:

```bash
# 1. Generate core project skill
repomix src/ --skill-generate --compress

# 2. Generate test patterns skill (if useful)
repomix test/ --skill-generate --include "**/*.test.ts" --compress test-patterns

# 3. Add to gitignore if large
echo ".claude/skills/repomix-reference-*/" >> .gitignore

# 4. Or commit if small enough for team
git add .claude/skills/
git commit -m "Add Claude skills for project reference"
```

## Troubleshooting

### Skill Not Loading

1. Check location is correct (`~/.claude/skills/` or `.claude/skills/`)
2. Verify SKILL.md exists and has valid frontmatter
3. Check description has relevant trigger phrases

### Files Too Large

1. Use `--compress` flag
2. Filter with `--include` and `--ignore`
3. Split into multiple smaller Skills
4. Use `--remove-comments --remove-empty-lines`

### Stale Content

Skills are snapshots - regenerate after significant changes:

```bash
rm -rf .claude/skills/old-skill/
repomix --skill-generate new-skill
```

## Summary

Agent Skills Generation transforms Repomix from a runtime tool into a **permanent knowledge base builder**. Key benefits:

1. **Zero runtime cost** - Generate once, reference forever
2. **Instant access** - No packing delay
3. **Shareable** - Commit to git for team access
4. **Searchable** - Grep-friendly file format
5. **Auto-detected** - Technology stack automatically identified

**Primary command:**
```bash
repomix --skill-generate [skill-name]
```

This is the most efficient way to give Claude persistent, searchable knowledge about any codebase.
