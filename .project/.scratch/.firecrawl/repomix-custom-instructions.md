[Skip to content](https://repomix.com/guide/custom-instructions#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/custom-instructions)

[繁體中文](https://repomix.com/zh-tw/guide/custom-instructions)

[日本語](https://repomix.com/ja/guide/custom-instructions)

[Español](https://repomix.com/es/guide/custom-instructions)

[Português](https://repomix.com/pt-br/guide/custom-instructions)

[한국어](https://repomix.com/ko/guide/custom-instructions)

[Deutsch](https://repomix.com/de/guide/custom-instructions)

[Français](https://repomix.com/fr/guide/custom-instructions)

[Italiano](https://repomix.com/it/guide/custom-instructions)

[हिन्दी](https://repomix.com/hi/guide/custom-instructions)

[Indonesia](https://repomix.com/id/guide/custom-instructions)

[Tiếng Việt](https://repomix.com/vi/guide/custom-instructions)

[Русский](https://repomix.com/ru/guide/custom-instructions)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/custom-instructions)

[繁體中文](https://repomix.com/zh-tw/guide/custom-instructions)

[日本語](https://repomix.com/ja/guide/custom-instructions)

[Español](https://repomix.com/es/guide/custom-instructions)

[Português](https://repomix.com/pt-br/guide/custom-instructions)

[한국어](https://repomix.com/ko/guide/custom-instructions)

[Deutsch](https://repomix.com/de/guide/custom-instructions)

[Français](https://repomix.com/fr/guide/custom-instructions)

[Italiano](https://repomix.com/it/guide/custom-instructions)

[हिन्दी](https://repomix.com/hi/guide/custom-instructions)

[Indonesia](https://repomix.com/id/guide/custom-instructions)

[Tiếng Việt](https://repomix.com/vi/guide/custom-instructions)

[Русский](https://repomix.com/ru/guide/custom-instructions)

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

- [Usage](https://repomix.com/guide/custom-instructions#usage "Usage")
- [Example](https://repomix.com/guide/custom-instructions#example "Example")

# Custom Instructions [​](https://repomix.com/guide/custom-instructions\#custom-instructions)

Repomix allows you to provide custom instructions that will be included in the output file. This can be useful for adding context or specific guidelines for AI systems processing the repository.

## Usage [​](https://repomix.com/guide/custom-instructions\#usage)

To include a custom instruction, create a markdown file (e.g., `repomix-instruction.md`) in the root of your repository. Then, specify the path to this file in your `repomix.config.json`:

json

```
{
  "output": {
    "instructionFilePath": "repomix-instruction.md"
  }
}
```

The content of this file will be included in the output under the "Instruction" section.

## Example [​](https://repomix.com/guide/custom-instructions\#example)

markdown

```
# Repository Instructions

This repository contains the source code for the Repomix tool. Please follow these guidelines when analyzing the code:

1. Focus on the core functionality in the `src/core` directory.
2. Pay special attention to the security checks in `src/core/security`.
3. Ignore any files in the `tests` directory.
```

This will result in the following section in the output:

xml

```
<instruction>
# Repository Instructions

This repository contains the source code for the Repomix tool. Please follow these guidelines when analyzing the code:

1. Focus on the core functionality in the `src/core` directory.
2. Pay special attention to the security checks in `src/core/security`.
3. Ignore any files in the `tests` directory.
</instruction>
```

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageConfiguration](https://repomix.com/guide/configuration)

[Next pageGitHub Repository Processing](https://repomix.com/guide/remote-repository-processing)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada