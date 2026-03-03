---
name: repomix
description: |
  Pack a codebase into a single AI-optimized file using Repomix. Use when you
  need to prepare a repository for AI analysis: packing local or remote repos,
  selecting files with glob patterns, compressing code structure, filtering with
  include/ignore patterns, adding git context (diffs, logs), analyzing token
  distribution, or generating output in XML, Markdown, JSON, or plain text.
  Also use when configuring repomix.config.json, diagnosing token budget issues,
  or splitting large outputs for tools with file size limits.
---

# Repomix

Repomix packs a repository into a single file optimized for LLM consumption.
Install: `npm install -g repomix`

## Quick start

```bash
repomix                          # Pack current directory → repomix-output.xml
repomix path/to/directory        # Pack specific directory
repomix --stdout | llm "..."     # Pipe directly into an LLM CLI
```

## Common patterns

### Include / ignore files

```bash
repomix --include "src/**/*.ts,**/*.md"
repomix --ignore "**/*.test.ts,docs/**"

# Combine: pack only source TypeScript, skip tests
repomix --include "src/**/*.ts" --ignore "**/*.spec.ts"
```

### Remote repositories

```bash
repomix --remote user/repo                       # GitHub shorthand
repomix --remote https://github.com/user/repo
repomix --remote user/repo --remote-branch main
repomix --remote user/repo --remote-branch 935b695  # specific commit
```

### Code compression (Tree-sitter)

Strips function bodies, keeps signatures, interfaces, class structures. Reduces
token count significantly while preserving architectural context.

```bash
repomix --compress
repomix --remote user/repo --compress
```

Compression preserves: function/method signatures, interfaces, type definitions,
class structures. Removes: implementations, loop bodies, internal variables.

### Stdin - precise file selection

```bash
git ls-files "*.ts" | repomix --stdin           # only git-tracked TS files
find src -name "*.cs" | repomix --stdin         # find output → repomix
rg -l "TODO|FIXME" --type ts | repomix --stdin  # files matching content
fzf -m | repomix --stdin                        # interactive selection
```

Stdin files still obey ignore patterns. Paths can be relative or absolute.

### Git context

```bash
repomix --include-diffs                         # uncommitted changes
repomix --include-logs                          # last 50 commits
repomix --include-logs --include-logs-count 10  # last 10 commits
repomix --include-diffs --include-logs          # both
```

Git logs show which files change together, commit messages, and development focus.

### Split output for large repos

Some AI tools enforce file size limits (e.g., Google AI Studio: 1MB).

```bash
repomix --split-output 1mb     # → repomix-output.1.xml, .2.xml, .3.xml
repomix --split-output 500kb
```

Files are grouped by top-level directory - a single file is never split across parts.

### Token count analysis

```bash
repomix --token-count-tree          # full tree with token counts
repomix --token-count-tree 1000     # only files/dirs with ≥1000 tokens
```

Use this to identify token-heavy files before applying `--include`/`--ignore` to
trim the output, or to decide where `--compress` has the most impact.

## Output formats

| Format | Flag | Best for |
|--------|------|----------|
| XML (default) | `--style xml` | Claude, most LLMs |
| Markdown | `--style markdown` | Human-readable review |
| JSON | `--style json` | Programmatic processing |
| Plain text | `--style plain` | Simple tools |

```bash
repomix -o output.md --style markdown
repomix -o output.json --style json
repomix --stdout > output.txt          # stdout instead of file
```

## Token reduction checklist

When token count is too high, apply in order:
1. `--compress` - biggest impact; strips implementations
2. `--remove-comments` - removes all code comments
3. `--remove-empty-lines` - removes blank lines
4. `--ignore "**/*.test.*,docs/**"` - exclude low-value files
5. `--truncate-base64` - truncates embedded images/data
6. `--include "src/**/*"` - scope to relevant directories only
7. `--split-output 1mb` - split if still too large for the tool

## Configuration file

```bash
repomix --init           # create repomix.config.json with defaults
repomix --init --global  # create global config (~/.config/repomix/)
```

Config file lookup order: TypeScript → JavaScript → JSON (local first, then global).
CLI flags override config file settings.

For full config options: [references/configuration.md](references/configuration.md)
For all CLI flags: [references/cli-options.md](references/cli-options.md)
