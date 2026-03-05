[Skip to content](https://repomix.com/guide/command-line-options#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/command-line-options)

[繁體中文](https://repomix.com/zh-tw/guide/command-line-options)

[日本語](https://repomix.com/ja/guide/command-line-options)

[Español](https://repomix.com/es/guide/command-line-options)

[Português](https://repomix.com/pt-br/guide/command-line-options)

[한국어](https://repomix.com/ko/guide/command-line-options)

[Deutsch](https://repomix.com/de/guide/command-line-options)

[Français](https://repomix.com/fr/guide/command-line-options)

[Italiano](https://repomix.com/it/guide/command-line-options)

[हिन्दी](https://repomix.com/hi/guide/command-line-options)

[Indonesia](https://repomix.com/id/guide/command-line-options)

[Tiếng Việt](https://repomix.com/vi/guide/command-line-options)

[Русский](https://repomix.com/ru/guide/command-line-options)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/command-line-options)

[繁體中文](https://repomix.com/zh-tw/guide/command-line-options)

[日本語](https://repomix.com/ja/guide/command-line-options)

[Español](https://repomix.com/es/guide/command-line-options)

[Português](https://repomix.com/pt-br/guide/command-line-options)

[한국어](https://repomix.com/ko/guide/command-line-options)

[Deutsch](https://repomix.com/de/guide/command-line-options)

[Français](https://repomix.com/fr/guide/command-line-options)

[Italiano](https://repomix.com/it/guide/command-line-options)

[हिन्दी](https://repomix.com/hi/guide/command-line-options)

[Indonesia](https://repomix.com/id/guide/command-line-options)

[Tiếng Việt](https://repomix.com/vi/guide/command-line-options)

[Русский](https://repomix.com/ru/guide/command-line-options)

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

- [Basic Options](https://repomix.com/guide/command-line-options#basic-options "Basic Options")
- [CLI Input/Output Options](https://repomix.com/guide/command-line-options#cli-input-output-options "CLI Input/Output Options")
- [Repomix Output Options](https://repomix.com/guide/command-line-options#repomix-output-options "Repomix Output Options")
- [File Selection Options](https://repomix.com/guide/command-line-options#file-selection-options "File Selection Options")
- [Remote Repository Options](https://repomix.com/guide/command-line-options#remote-repository-options "Remote Repository Options")
- [Configuration Options](https://repomix.com/guide/command-line-options#configuration-options "Configuration Options")
- [Security Options](https://repomix.com/guide/command-line-options#security-options "Security Options")
- [Token Count Options](https://repomix.com/guide/command-line-options#token-count-options "Token Count Options")
- [MCP Options](https://repomix.com/guide/command-line-options#mcp-options "MCP Options")
- [Examples](https://repomix.com/guide/command-line-options#examples "Examples")

# Command Line Options [​](https://repomix.com/guide/command-line-options\#command-line-options)

## Basic Options [​](https://repomix.com/guide/command-line-options\#basic-options)

- `-v, --version`: Show version information and exit

## CLI Input/Output Options [​](https://repomix.com/guide/command-line-options\#cli-input-output-options)

- `--verbose`: Enable detailed debug logging (shows file processing, token counts, and configuration details)
- `--quiet`: Suppress all console output except errors (useful for scripting)
- `--stdout`: Write packed output directly to stdout instead of a file (suppresses all logging)
- `--stdin`: Read file paths from stdin, one per line (specified files are processed directly)
- `--copy`: Copy the generated output to system clipboard after processing
- `--token-count-tree [threshold]`: Show file tree with token counts; optional threshold to show only files with ≥N tokens (e.g., --token-count-tree 100)
- `--top-files-len <number>`: Number of largest files to show in summary (default: 5, e.g., --top-files-len 20)

## Repomix Output Options [​](https://repomix.com/guide/command-line-options\#repomix-output-options)

- `-o, --output <file>`: Output file path (default: repomix-output.xml, use "-" for stdout)
- `--style <style>`: Output format: xml, markdown, json, or plain (default: xml)
- `--parsable-style`: Escape special characters to ensure valid XML/Markdown (needed when output contains code that breaks formatting)
- `--compress`: Extract essential code structure (classes, functions, interfaces) using Tree-sitter parsing
- `--output-show-line-numbers`: Prefix each line with its line number in the output
- `--no-file-summary`: Omit the file summary section from output
- `--no-directory-structure`: Omit the directory tree visualization from output
- `--no-files`: Generate metadata only without file contents (useful for repository analysis)
- `--remove-comments`: Strip all code comments before packing
- `--remove-empty-lines`: Remove blank lines from all files
- `--truncate-base64`: Truncate long base64 data strings to reduce output size
- `--header-text <text>`: Custom text to include at the beginning of the output
- `--instruction-file-path <path>`: Path to file containing custom instructions to include in output
- `--split-output <size>`: Split output into multiple numbered files (e.g., repomix-output.1.xml, repomix-output.2.xml); size like 500kb, 2mb, or 1.5mb
- `--include-empty-directories`: Include folders with no files in directory structure
- `--include-full-directory-structure`: Show entire repository tree in the Directory Structure section, even when using --include patterns
- `--no-git-sort-by-changes`: Don't sort files by git change frequency (default: most changed files first)
- `--include-diffs`: Add git diff section showing working tree and staged changes
- `--include-logs`: Add git commit history with messages and changed files
- `--include-logs-count <count>`: Number of recent commits to include with --include-logs (default: 50)

## File Selection Options [​](https://repomix.com/guide/command-line-options\#file-selection-options)

- `--include <patterns>`: Include only files matching these glob patterns (comma-separated, e.g., "src/\*\*/ _.js,_.md")
- `-i, --ignore <patterns>`: Additional patterns to exclude (comma-separated, e.g., "\*.test.js,docs/\*\*")
- `--no-gitignore`: Don't use .gitignore rules for filtering files
- `--no-dot-ignore`: Don't use .ignore rules for filtering files
- `--no-default-patterns`: Don't apply built-in ignore patterns (node\_modules, .git, build dirs, etc.)

## Remote Repository Options [​](https://repomix.com/guide/command-line-options\#remote-repository-options)

- `--remote <url>`: Clone and pack a remote repository (GitHub URL or user/repo format)
- `--remote-branch <name>`: Specific branch, tag, or commit to use (default: repository's default branch)

## Configuration Options [​](https://repomix.com/guide/command-line-options\#configuration-options)

- `-c, --config <path>`: Use custom config file instead of repomix.config.json
- `--init`: Create a new repomix.config.json file with defaults
- `--global`: With --init, create config in home directory instead of current directory

## Security Options [​](https://repomix.com/guide/command-line-options\#security-options)

- `--no-security-check`: Skip scanning for sensitive data like API keys and passwords (use with caution; may expose secrets in output)

## Token Count Options [​](https://repomix.com/guide/command-line-options\#token-count-options)

- `--token-count-encoding <encoding>`: Tokenizer model for counting: o200k\_base (GPT-4o), cl100k\_base (GPT-3.5/4), etc. (default: o200k\_base)

## MCP Options [​](https://repomix.com/guide/command-line-options\#mcp-options)

- `--mcp`: Run as Model Context Protocol server for AI tool integration

## Examples [​](https://repomix.com/guide/command-line-options\#examples)

bash

```
# Basic usage
repomix

# Custom output file and format
repomix -o my-output.md --style markdown
repomix -o my-output.json --style json

# Output to stdout
repomix --stdout > custom-output.txt

# Send output to stdout, then pipe into another command (for example, simonw/llm)
repomix --stdout | llm "Please explain what this code does."

# Custom output with compression
repomix --compress

# Split output into multiple files (max size per part)
repomix --split-output 20mb

# Process specific files with patterns
repomix --include "src/**/*.ts,*.md" --ignore "*.test.js,docs/**"

# Remote repository with branch
repomix --remote https://github.com/user/repo/tree/main

# Remote repository with commit
repomix --remote https://github.com/user/repo/commit/836abcd7335137228ad77feb28655d85712680f1

# Remote repository with shorthand
repomix --remote user/repo

# Using stdin for file list
find src -name "*.ts" -type f | repomix --stdin
git ls-files "*.js" | repomix --stdin
echo -e "src/index.ts\nsrc/utils.ts" | repomix --stdin

# Git integration
repomix --include-diffs  # Include git diffs for uncommitted changes
repomix --include-logs   # Include git logs (last 50 commits by default)
repomix --include-logs --include-logs-count 10  # Include last 10 commits
repomix --include-diffs --include-logs  # Include both diffs and logs

# Token count analysis
repomix --token-count-tree
repomix --token-count-tree 1000  # Only show files/directories with 1000+ tokens
```

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageOutput Formats](https://repomix.com/guide/output)

[Next pageConfiguration](https://repomix.com/guide/configuration)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada