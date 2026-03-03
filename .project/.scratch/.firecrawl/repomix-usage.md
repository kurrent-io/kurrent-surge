[Skip to content](https://repomix.com/guide/usage#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/usage)

[繁體中文](https://repomix.com/zh-tw/guide/usage)

[日本語](https://repomix.com/ja/guide/usage)

[Español](https://repomix.com/es/guide/usage)

[Português](https://repomix.com/pt-br/guide/usage)

[한국어](https://repomix.com/ko/guide/usage)

[Deutsch](https://repomix.com/de/guide/usage)

[Français](https://repomix.com/fr/guide/usage)

[Italiano](https://repomix.com/it/guide/usage)

[हिन्दी](https://repomix.com/hi/guide/usage)

[Indonesia](https://repomix.com/id/guide/usage)

[Tiếng Việt](https://repomix.com/vi/guide/usage)

[Русский](https://repomix.com/ru/guide/usage)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/usage)

[繁體中文](https://repomix.com/zh-tw/guide/usage)

[日本語](https://repomix.com/ja/guide/usage)

[Español](https://repomix.com/es/guide/usage)

[Português](https://repomix.com/pt-br/guide/usage)

[한국어](https://repomix.com/ko/guide/usage)

[Deutsch](https://repomix.com/de/guide/usage)

[Français](https://repomix.com/fr/guide/usage)

[Italiano](https://repomix.com/it/guide/usage)

[हिन्दी](https://repomix.com/hi/guide/usage)

[Indonesia](https://repomix.com/id/guide/usage)

[Tiếng Việt](https://repomix.com/vi/guide/usage)

[Русский](https://repomix.com/ru/guide/usage)

Appearance

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

Menu

On this page

Sidebar Navigation

## Introduction

[Getting Started](https://repomix.com/guide/)

[Installation](https://repomix.com/guide/installation)

[Basic Usage](https://repomix.com/guide/usage)

[Prompt Examples](https://repomix.com/guide/prompt-examples)

[Use Cases](https://repomix.com/guide/use-cases)

## Guide

[Output Formats](https://repomix.com/guide/output)

[Command Line Options](https://repomix.com/guide/command-line-options)

[Configuration](https://repomix.com/guide/configuration)

[Custom Instructions](https://repomix.com/guide/custom-instructions)

[GitHub Repository Processing](https://repomix.com/guide/remote-repository-processing)

[Comment Removal](https://repomix.com/guide/comment-removal)

[Code Compression](https://repomix.com/guide/code-compress)

[Security](https://repomix.com/guide/security)

## Advanced

[MCP Server](https://repomix.com/guide/mcp-server)

[Claude Code Plugins](https://repomix.com/guide/claude-code-plugins)

[Agent Skills Generation](https://repomix.com/guide/agent-skills-generation)

[Repomix Explorer Skill](https://repomix.com/guide/repomix-explorer-skill)

[GitHub Actions](https://repomix.com/guide/github-actions)

[Using Repomix as a Library](https://repomix.com/guide/development/using-repomix-as-a-library)

[AI-Assisted Development Tips](https://repomix.com/guide/tips/best-practices)

## Community

[Community Projects](https://repomix.com/guide/community-projects)

[Contributing to Repomix](https://repomix.com/guide/development/)

[Sponsors](https://repomix.com/guide/sponsors)

[Privacy Policy](https://repomix.com/guide/privacy)

On this page

- [Quick Start](https://repomix.com/guide/usage#quick-start "Quick Start")
- [Common Use Cases](https://repomix.com/guide/usage#common-use-cases "Common Use Cases")
  - [Pack Specific Directories](https://repomix.com/guide/usage#pack-specific-directories "Pack Specific Directories")
  - [Include Specific Files](https://repomix.com/guide/usage#include-specific-files "Include Specific Files")
  - [Exclude Files](https://repomix.com/guide/usage#exclude-files "Exclude Files")
  - [Split Output Into Multiple Files](https://repomix.com/guide/usage#split-output-into-multiple-files "Split Output Into Multiple Files")
  - [Remote Repositories](https://repomix.com/guide/usage#remote-repositories "Remote Repositories")
  - [File List Input (stdin)](https://repomix.com/guide/usage#file-list-input-stdin "File List Input (stdin)")
  - [Code Compression](https://repomix.com/guide/usage#code-compression "Code Compression")
  - [Git Integration](https://repomix.com/guide/usage#git-integration "Git Integration")
  - [Token Count Optimization](https://repomix.com/guide/usage#token-count-optimization "Token Count Optimization")
- [Output Formats](https://repomix.com/guide/usage#output-formats "Output Formats")
  - [XML (Default)](https://repomix.com/guide/usage#xml-default "XML (Default)")
  - [Markdown](https://repomix.com/guide/usage#markdown "Markdown")
  - [JSON](https://repomix.com/guide/usage#json "JSON")
  - [Plain Text](https://repomix.com/guide/usage#plain-text "Plain Text")
- [Additional Options](https://repomix.com/guide/usage#additional-options "Additional Options")
  - [Remove Comments](https://repomix.com/guide/usage#remove-comments "Remove Comments")
  - [Show Line Numbers](https://repomix.com/guide/usage#show-line-numbers "Show Line Numbers")
  - [Copy to Clipboard](https://repomix.com/guide/usage#copy-to-clipboard "Copy to Clipboard")
  - [Disable Security Check](https://repomix.com/guide/usage#disable-security-check "Disable Security Check")
- [Configuration](https://repomix.com/guide/usage#configuration "Configuration")

# Basic Usage [​](https://repomix.com/guide/usage\#basic-usage)

## Quick Start [​](https://repomix.com/guide/usage\#quick-start)

Pack your entire repository:

bash

```
repomix
```

## Common Use Cases [​](https://repomix.com/guide/usage\#common-use-cases)

### Pack Specific Directories [​](https://repomix.com/guide/usage\#pack-specific-directories)

bash

```
repomix path/to/directory
```

### Include Specific Files [​](https://repomix.com/guide/usage\#include-specific-files)

Use [glob patterns](https://github.com/mrmlnc/fast-glob?tab=readme-ov-file#pattern-syntax):

bash

```
repomix --include "src/**/*.ts,**/*.md"
```

### Exclude Files [​](https://repomix.com/guide/usage\#exclude-files)

bash

```
repomix --ignore "**/*.log,tmp/"
```

### Split Output Into Multiple Files [​](https://repomix.com/guide/usage\#split-output-into-multiple-files)

When working with large codebases, the packed output may exceed file size limits imposed by some AI tools (e.g., Google AI Studio's 1MB limit). Use `--split-output` to automatically split the output into multiple files:

bash

```
repomix --split-output 1mb
```

This generates numbered files like:

- `repomix-output.1.xml`
- `repomix-output.2.xml`
- `repomix-output.3.xml`

Size can be specified with units: `500kb`, `1mb`, `2mb`, `1.5mb`, etc. Decimal values are supported.

NOTE

Files are grouped by top-level directory to maintain context. A single file or directory will never be split across multiple output files.

### Remote Repositories [​](https://repomix.com/guide/usage\#remote-repositories)

bash

```
# Using GitHub URL
repomix --remote https://github.com/user/repo

# Using shorthand
repomix --remote user/repo

# Specific branch/tag/commit
repomix --remote user/repo --remote-branch main
repomix --remote user/repo --remote-branch 935b695
```

### File List Input (stdin) [​](https://repomix.com/guide/usage\#file-list-input-stdin)

Pass file paths via stdin for ultimate flexibility:

bash

```
# Using find command
find src -name "*.ts" -type f | repomix --stdin

# Using git to get tracked files
git ls-files "*.ts" | repomix --stdin

# Using ripgrep (rg) to find files
rg --files --type ts | repomix --stdin

# Using grep to find files containing specific content
grep -l "TODO" **/*.ts | repomix --stdin

# Using ripgrep to find files with specific content
rg -l "TODO|FIXME" --type ts | repomix --stdin

# Using sharkdp/fd to find files
fd -e ts | repomix --stdin

# Using fzf to select from all files
fzf -m | repomix --stdin

# Interactive file selection with fzf
find . -name "*.ts" -type f | fzf -m | repomix --stdin

# Using ls with glob patterns
ls src/**/*.ts | repomix --stdin

# From a file containing file paths
cat file-list.txt | repomix --stdin

# Direct input with echo
echo -e "src/index.ts\nsrc/utils.ts" | repomix --stdin
```

The `--stdin` option allows you to pipe a list of file paths to Repomix, giving you ultimate flexibility in selecting which files to pack.

When using `--stdin`, the specified files are effectively added to the include patterns. This means that the normal include and ignore behavior still applies - files specified via stdin will still be excluded if they match ignore patterns.

NOTE

When using `--stdin`, file paths can be relative or absolute, and Repomix will automatically handle path resolution and deduplication.

### Code Compression [​](https://repomix.com/guide/usage\#code-compression)

bash

```
repomix --compress

# You can also use it with remote repositories:
repomix --remote yamadashy/repomix --compress
```

### Git Integration [​](https://repomix.com/guide/usage\#git-integration)

Include Git information to provide development context for AI analysis:

bash

```
# Include git diffs (uncommitted changes)
repomix --include-diffs

# Include git commit logs (last 50 commits by default)
repomix --include-logs

# Include specific number of commits
repomix --include-logs --include-logs-count 10

# Include both diffs and logs
repomix --include-diffs --include-logs
```

This adds valuable context about:

- **Recent changes**: Git diffs show uncommitted modifications
- **Development patterns**: Git logs reveal which files are typically changed together
- **Commit history**: Recent commit messages provide insight into development focus
- **File relationships**: Understanding which files are modified in the same commits

### Token Count Optimization [​](https://repomix.com/guide/usage\#token-count-optimization)

Understanding your codebase's token distribution is crucial for optimizing AI interactions. Use the `--token-count-tree` option to visualize token usage across your project:

bash

```
repomix --token-count-tree
```

This displays a hierarchical view of your codebase with token counts:

```
🔢 Token Count Tree:
────────────────────
└── src/ (70,925 tokens)
    ├── cli/ (12,714 tokens)
    │   ├── actions/ (7,546 tokens)
    │   └── reporters/ (990 tokens)
    └── core/ (41,600 tokens)
        ├── file/ (10,098 tokens)
        └── output/ (5,808 tokens)
```

You can also set a minimum token threshold to focus on larger files:

bash

```
repomix --token-count-tree 1000  # Only show files/directories with 1000+ tokens
```

This helps you:

- **Identify token-heavy files** that might exceed AI context limits
- **Optimize file selection** using `--include` and `--ignore` patterns
- **Plan compression strategies** by targeting the largest contributors
- **Balance content vs. context** when preparing code for AI analysis

## Output Formats [​](https://repomix.com/guide/usage\#output-formats)

### XML (Default) [​](https://repomix.com/guide/usage\#xml-default)

bash

```
repomix --style xml
```

### Markdown [​](https://repomix.com/guide/usage\#markdown)

bash

```
repomix --style markdown
```

### JSON [​](https://repomix.com/guide/usage\#json)

bash

```
repomix --style json
```

### Plain Text [​](https://repomix.com/guide/usage\#plain-text)

bash

```
repomix --style plain
```

## Additional Options [​](https://repomix.com/guide/usage\#additional-options)

### Remove Comments [​](https://repomix.com/guide/usage\#remove-comments)

bash

```
repomix --remove-comments
```

### Show Line Numbers [​](https://repomix.com/guide/usage\#show-line-numbers)

bash

```
repomix --output-show-line-numbers
```

### Copy to Clipboard [​](https://repomix.com/guide/usage\#copy-to-clipboard)

bash

```
repomix --copy
```

### Disable Security Check [​](https://repomix.com/guide/usage\#disable-security-check)

bash

```
repomix --no-security-check
```

## Configuration [​](https://repomix.com/guide/usage\#configuration)

Initialize configuration file:

bash

```
repomix --init
```

See [Configuration Guide](https://repomix.com/guide/configuration) for detailed options.

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageInstallation](https://repomix.com/guide/installation)

[Next pagePrompt Examples](https://repomix.com/guide/prompt-examples)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada