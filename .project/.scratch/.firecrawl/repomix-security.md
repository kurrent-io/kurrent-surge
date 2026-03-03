[Skip to content](https://repomix.com/guide/security#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/security)

[繁體中文](https://repomix.com/zh-tw/guide/security)

[日本語](https://repomix.com/ja/guide/security)

[Español](https://repomix.com/es/guide/security)

[Português](https://repomix.com/pt-br/guide/security)

[한국어](https://repomix.com/ko/guide/security)

[Deutsch](https://repomix.com/de/guide/security)

[Français](https://repomix.com/fr/guide/security)

[Italiano](https://repomix.com/it/guide/security)

[हिन्दी](https://repomix.com/hi/guide/security)

[Indonesia](https://repomix.com/id/guide/security)

[Tiếng Việt](https://repomix.com/vi/guide/security)

[Русский](https://repomix.com/ru/guide/security)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/security)

[繁體中文](https://repomix.com/zh-tw/guide/security)

[日本語](https://repomix.com/ja/guide/security)

[Español](https://repomix.com/es/guide/security)

[Português](https://repomix.com/pt-br/guide/security)

[한국어](https://repomix.com/ko/guide/security)

[Deutsch](https://repomix.com/de/guide/security)

[Français](https://repomix.com/fr/guide/security)

[Italiano](https://repomix.com/it/guide/security)

[हिन्दी](https://repomix.com/hi/guide/security)

[Indonesia](https://repomix.com/id/guide/security)

[Tiếng Việt](https://repomix.com/vi/guide/security)

[Русский](https://repomix.com/ru/guide/security)

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

- [Security Check Feature](https://repomix.com/guide/security#security-check-feature "Security Check Feature")
- [Configuration](https://repomix.com/guide/security#configuration "Configuration")
- [Security Measures](https://repomix.com/guide/security#security-measures "Security Measures")
- [When Security Check Finds Issues](https://repomix.com/guide/security#when-security-check-finds-issues "When Security Check Finds Issues")
- [Best Practices](https://repomix.com/guide/security#best-practices "Best Practices")
- [Reporting Security Issues](https://repomix.com/guide/security#reporting-security-issues "Reporting Security Issues")

# Security [​](https://repomix.com/guide/security\#security)

## Security Check Feature [​](https://repomix.com/guide/security\#security-check-feature)

Repomix uses [Secretlint](https://github.com/secretlint/secretlint) to detect sensitive information in your files:

- API keys
- Access tokens
- Credentials
- Private keys
- Environment variables

## Configuration [​](https://repomix.com/guide/security\#configuration)

Security checks are enabled by default.

Disable via CLI:

bash

```
repomix --no-security-check
```

Or in `repomix.config.json`:

json

```
{
  "security": {
    "enableSecurityCheck": false
  }
}
```

## Security Measures [​](https://repomix.com/guide/security\#security-measures)

1. **Binary File Handling**: Binary file contents are excluded from output, but their paths are listed in the directory structure for complete repository overview
2. **Git-Aware**: Respects `.gitignore` patterns
3. **Automated Detection**: Scans for common security issues:

- AWS credentials
- Database connection strings
- Authentication tokens
- Private keys

## When Security Check Finds Issues [​](https://repomix.com/guide/security\#when-security-check-finds-issues)

Example output:

bash

```
🔍 Security Check:
──────────────────
2 suspicious file(s) detected and excluded:
1. config/credentials.json
  - Found AWS access key
2. .env.local
  - Found database password
```

## Best Practices [​](https://repomix.com/guide/security\#best-practices)

1. Always review output before sharing
2. Use `.repomixignore` for sensitive paths
3. Keep security checks enabled
4. Remove sensitive files from repository

## Reporting Security Issues [​](https://repomix.com/guide/security\#reporting-security-issues)

Found a security vulnerability? Please:

1. Do not open a public issue
2. Email: [koukun0120@gmail.com](mailto:koukun0120@gmail.com)
3. Or use [GitHub Security Advisories](https://github.com/yamadashy/repomix/security/advisories/new)

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageCode Compression](https://repomix.com/guide/code-compress)

[Next pageMCP Server](https://repomix.com/guide/mcp-server)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada