[Skip to content](https://repomix.com/guide/remote-repository-processing#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/remote-repository-processing)

[繁體中文](https://repomix.com/zh-tw/guide/remote-repository-processing)

[日本語](https://repomix.com/ja/guide/remote-repository-processing)

[Español](https://repomix.com/es/guide/remote-repository-processing)

[Português](https://repomix.com/pt-br/guide/remote-repository-processing)

[한국어](https://repomix.com/ko/guide/remote-repository-processing)

[Deutsch](https://repomix.com/de/guide/remote-repository-processing)

[Français](https://repomix.com/fr/guide/remote-repository-processing)

[Italiano](https://repomix.com/it/guide/remote-repository-processing)

[हिन्दी](https://repomix.com/hi/guide/remote-repository-processing)

[Indonesia](https://repomix.com/id/guide/remote-repository-processing)

[Tiếng Việt](https://repomix.com/vi/guide/remote-repository-processing)

[Русский](https://repomix.com/ru/guide/remote-repository-processing)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/remote-repository-processing)

[繁體中文](https://repomix.com/zh-tw/guide/remote-repository-processing)

[日本語](https://repomix.com/ja/guide/remote-repository-processing)

[Español](https://repomix.com/es/guide/remote-repository-processing)

[Português](https://repomix.com/pt-br/guide/remote-repository-processing)

[한국어](https://repomix.com/ko/guide/remote-repository-processing)

[Deutsch](https://repomix.com/de/guide/remote-repository-processing)

[Français](https://repomix.com/fr/guide/remote-repository-processing)

[Italiano](https://repomix.com/it/guide/remote-repository-processing)

[हिन्दी](https://repomix.com/hi/guide/remote-repository-processing)

[Indonesia](https://repomix.com/id/guide/remote-repository-processing)

[Tiếng Việt](https://repomix.com/vi/guide/remote-repository-processing)

[Русский](https://repomix.com/ru/guide/remote-repository-processing)

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

- [Basic Usage](https://repomix.com/guide/remote-repository-processing#basic-usage "Basic Usage")
- [Branch and Commit Selection](https://repomix.com/guide/remote-repository-processing#branch-and-commit-selection "Branch and Commit Selection")
- [Requirements](https://repomix.com/guide/remote-repository-processing#requirements "Requirements")
- [Output Control](https://repomix.com/guide/remote-repository-processing#output-control "Output Control")
- [Docker Usage](https://repomix.com/guide/remote-repository-processing#docker-usage "Docker Usage")
- [Common Issues](https://repomix.com/guide/remote-repository-processing#common-issues "Common Issues")
  - [Access Issues](https://repomix.com/guide/remote-repository-processing#access-issues "Access Issues")
  - [Large Repositories](https://repomix.com/guide/remote-repository-processing#large-repositories "Large Repositories")

# GitHub Repository Processing [​](https://repomix.com/guide/remote-repository-processing\#github-repository-processing)

## Basic Usage [​](https://repomix.com/guide/remote-repository-processing\#basic-usage)

Process public repositories:

bash

```
# Using full URL
repomix --remote https://github.com/user/repo

# Using GitHub shorthand
repomix --remote user/repo
```

## Branch and Commit Selection [​](https://repomix.com/guide/remote-repository-processing\#branch-and-commit-selection)

bash

```
# Specific branch
repomix --remote user/repo --remote-branch main

# Tag
repomix --remote user/repo --remote-branch v1.0.0

# Commit hash
repomix --remote user/repo --remote-branch 935b695
```

## Requirements [​](https://repomix.com/guide/remote-repository-processing\#requirements)

- Git must be installed
- Internet connection
- Read access to repository

## Output Control [​](https://repomix.com/guide/remote-repository-processing\#output-control)

bash

```
# Custom output location
repomix --remote user/repo -o custom-output.xml

# With XML format
repomix --remote user/repo --style xml

# Remove comments
repomix --remote user/repo --remove-comments
```

## Docker Usage [​](https://repomix.com/guide/remote-repository-processing\#docker-usage)

bash

```
# Process and output to current directory
docker run -v .:/app -it --rm ghcr.io/yamadashy/repomix \
  --remote user/repo

# Output to specific directory
docker run -v ./output:/app -it --rm ghcr.io/yamadashy/repomix \
  --remote user/repo
```

## Common Issues [​](https://repomix.com/guide/remote-repository-processing\#common-issues)

### Access Issues [​](https://repomix.com/guide/remote-repository-processing\#access-issues)

- Ensure repository is public
- Check Git installation
- Verify internet connection

### Large Repositories [​](https://repomix.com/guide/remote-repository-processing\#large-repositories)

- Use `--include` to select specific paths
- Enable `--remove-comments`
- Process branches separately

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageCustom Instructions](https://repomix.com/guide/custom-instructions)

[Next pageComment Removal](https://repomix.com/guide/comment-removal)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada