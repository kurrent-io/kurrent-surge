# Repomix CLI Reference

Comprehensive reference for all Repomix command-line options and flags.

## Basic Usage

```bash
# Pack current directory
npx repomix

# Pack specific directory
repomix path/to/project

# Pack remote repository
repomix --remote user/repo
repomix --remote https://github.com/user/repo
```

## Output Options

### File Path and Format

```bash
# Specify output file
repomix -o output.xml
repomix --output output.xml

# Output to stdout (suppresses logging)
repomix --stdout

# Pipe to other commands
repomix --stdout | llm "Explain this codebase"

# Copy to clipboard
repomix --copy
```

### Output Style

```bash
# XML format (default)
repomix --style xml

# Markdown format
repomix --style markdown

# JSON format
repomix --style json

# Plain text format
repomix --style plain
```

### Output Control

```bash
# Parsable style (escape special characters for valid XML/Markdown)
repomix --parsable-style

# Show line numbers
repomix --output-show-line-numbers

# Include full directory structure (even non-matching files)
repomix --include-full-directory-structure

# Include empty directories
repomix --include-empty-directories
```

### Split Output

Split large packed outputs into multiple numbered files. Useful when AI tools have file size limits (e.g., Google AI Studio's 1MB cap).

```bash
# Split output into ~1MB files
repomix --split-output 1mb

# Split into 500KB files
repomix --split-output 500kb

# Split into 2MB files (decimal values supported)
repomix --split-output 2mb
repomix --split-output 1.5mb
```

**Output files:** `repomix-output.1.xml`, `repomix-output.2.xml`, etc.

**Behavior:**
- Files are grouped by top-level directory to maintain context
- A single file or directory is never split across multiple output files
- Configuration option: `output.splitOutput` (e.g., `1000000` for ~1MB per file)

## Code Processing

### Compression

```bash
# Enable Tree-sitter compression (~70% token reduction)
repomix --compress

# With remote repository
repomix --remote user/repo --compress
```

Tree-sitter compression extracts:
- Imports and exports
- Function/class signatures
- Type definitions
- Interfaces
- Comments and documentation

Removes:
- Function bodies and implementation details
- Most code logic

### Comment and Line Handling

```bash
# Remove comments from output
repomix --remove-comments

# Remove empty lines
repomix --remove-empty-lines

# Truncate base64 encoded content
repomix --truncate-base64
```

## File Selection

### Include Patterns

```bash
# Include specific patterns (glob syntax)
repomix --include "src/**/*.ts"
repomix --include "**/*.{js,ts,tsx}"
repomix --include "src/**,docs/**"
```

### Ignore Patterns

```bash
# Add custom ignore patterns
repomix --ignore "**/*.test.ts"
repomix --ignore "**/*.log,temp/**"

# Disable gitignore
repomix --no-gitignore

# Disable default patterns
repomix --no-default-ignore

# Read file paths from stdin
repomix --stdin
```

## Git Integration

### Change-Based Sorting

```bash
# Sort files by recent git changes
repomix --sort-by-changes

# Control number of commits analyzed
repomix --sort-by-changes-max-commits 50

# Disable automatic git-based file ordering
repomix --no-git-sort-by-changes
```

### Git Information Inclusion

```bash
# Include git diffs (uncommitted changes)
repomix --include-diffs

# Include git logs (commit history)
repomix --include-logs

# Control number of log entries
repomix --include-logs-count 20

# Combine both
repomix --include-diffs --include-logs --include-logs-count 10
```

## Remote Repository Options

### Repository Specification

```bash
# GitHub short form
repomix --remote user/repo

# Full URL
repomix --remote https://github.com/user/repo

# Specific branch
repomix --remote https://github.com/user/repo/tree/branch-name
repomix --remote user/repo --remote-branch branch-name

# Specific tag
repomix --remote https://github.com/user/repo/tree/v1.0.0
```

## Security

### Security Scanning

```bash
# Disable security checks (NOT RECOMMENDED)
repomix --no-security
```

Default behavior: Enabled. Uses Secretlint to detect:
- API keys
- Tokens
- Credentials
- Passwords
- Sensitive patterns

## Token Management

### Token Counting

```bash
# Display token count tree
repomix --token-count-tree

# Show only files/directories with 1000+ tokens (threshold)
repomix --token-count-tree 1000

# Higher threshold for large codebases
repomix --token-count-tree 5000
```

Token encoding: Uses `o200k_base` encoding (configurable in config file)

**Use Cases:**
- Identify largest files consuming context window
- Plan filtering strategy before packing
- Optimize compression targets

## Configuration

### Custom Instructions

```bash
# Include custom instruction file
repomix --instruction-file-path path/to/instructions.md
```

### Configuration File

```bash
# Initialize default configuration
repomix --init

# Uses: repomix.config.json, repomix.config.js, or repomix.config.ts
```

Configuration files checked in priority order:
1. `repomix.config.ts` (TypeScript)
2. `repomix.config.js` (JavaScript)
3. `repomix.config.json` (JSON)

## Output Control

### File Summary

```bash
# Control number of top files shown in summary
repomix --top-files-length 10
```

## Debugging and Development

### Verbose Output

```bash
# Enable debug logging
repomix --verbose
```

Shows:
- File processing details
- Token counts per file
- Configuration applied
- Processing steps

### Version Information

```bash
# Display version
repomix --version

# Display help
repomix --help
```

## MCP Server Mode

```bash
# Start as MCP server
repomix --mcp
```

Used for MCP server integration with Claude Code and other MCP clients.

## Claude Agent Skills Generation

Generate structured, reusable codebase references in Claude Agent Skills format. Creates AI-ready reference materials for any codebase.

```bash
# Generate with auto-naming
repomix --skill-generate

# Generate with custom name
repomix --skill-generate my-project-reference

# Generate from specific directory
repomix src/ --skill-generate

# Generate from remote repository
repomix --remote user/repo --skill-generate

# Combine with other options
repomix --skill-generate --compress --include "src/**/*.ts"
```

**Generated Structure:**

```
.claude/skills/<skill-name>/
├── SKILL.md                    # Metadata and statistics
└── references/
    ├── summary.md              # Purpose, format, statistics
    ├── project-structure.md    # Directory tree with line counts
    ├── files.md                # All file contents (grep-friendly)
    └── tech-stack.md           # Auto-detected languages and frameworks
```

**Auto-naming Examples:**
- `repomix src/ --skill-generate` → `repomix-reference-src`
- `repomix --remote user/repo --skill-generate` → `repomix-reference-repo`

**Use Cases:**
- Create reusable codebase references for AI assistants
- Share standardized project knowledge across teams
- Generate documentation snapshots for specific versions
- Build AI-ready reference packages from any repository

## Docker Usage

```bash
# Process and output to current directory
docker run -v .:/app -it --rm ghcr.io/yamadashy/repomix \
  --remote user/repo

# Output to specific directory
docker run -v ./output:/app -it --rm ghcr.io/yamadashy/repomix \
  --remote user/repo -o /app/output.xml
```

## Common Combinations

### Code Review

```bash
repomix --compress --include-diffs --include-logs-count 10 \
  --style markdown -o review.md
```

### Security Audit

```bash
repomix --include "**/*.{js,ts,py}" \
  --style xml -o security-audit.xml
```

### Documentation Generation

```bash
repomix --compress --include "src/**" --remove-comments \
  --style markdown -o docs.md
```

### AI Analysis Preparation

```bash
repomix --compress --stdout | llm "Analyze architecture"
```

### Remote Repository Analysis

```bash
repomix --remote yamadashy/repomix --compress \
  --include "src/**/*.ts" --style json -o analysis.json
```

## Exit Codes

- `0`: Success
- `1`: Error (file not found, invalid configuration, etc.)
- `2`: Security check failed (sensitive data detected)

## Performance Considerations

**Token Reduction:**
- Tree-sitter compression: ~70% reduction
- Comment removal: 10-30% reduction (varies by codebase)
- Empty line removal: 5-15% reduction

**File Size Limits:**
- Default max file size: 50MB (configurable)
- Files exceeding limit are skipped with warning

**Processing Speed:**
- Local: ~1000 files/second (varies by file size)
- Remote: Limited by git clone speed

## Troubleshooting

### Common Issues

**Output too large:**
```bash
# Use compression
repomix --compress

# Filter files
repomix --include "src/**/*.ts"

# Use token count tree to identify large files
repomix --token-count-tree
```

**Security check failures:**
```bash
# Review .repomixignore
# Add patterns for known safe files with sensitive patterns
# Example: test fixtures, documentation examples
```

**Missing files:**
```bash
# Check .gitignore patterns
repomix --no-gitignore

# Verify custom ignore patterns
repomix --verbose
```
