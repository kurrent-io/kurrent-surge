[Skip to content](https://repomix.com/guide/comment-removal#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/comment-removal)

[繁體中文](https://repomix.com/zh-tw/guide/comment-removal)

[日本語](https://repomix.com/ja/guide/comment-removal)

[Español](https://repomix.com/es/guide/comment-removal)

[Português](https://repomix.com/pt-br/guide/comment-removal)

[한국어](https://repomix.com/ko/guide/comment-removal)

[Deutsch](https://repomix.com/de/guide/comment-removal)

[Français](https://repomix.com/fr/guide/comment-removal)

[Italiano](https://repomix.com/it/guide/comment-removal)

[हिन्दी](https://repomix.com/hi/guide/comment-removal)

[Indonesia](https://repomix.com/id/guide/comment-removal)

[Tiếng Việt](https://repomix.com/vi/guide/comment-removal)

[Русский](https://repomix.com/ru/guide/comment-removal)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/comment-removal)

[繁體中文](https://repomix.com/zh-tw/guide/comment-removal)

[日本語](https://repomix.com/ja/guide/comment-removal)

[Español](https://repomix.com/es/guide/comment-removal)

[Português](https://repomix.com/pt-br/guide/comment-removal)

[한국어](https://repomix.com/ko/guide/comment-removal)

[Deutsch](https://repomix.com/de/guide/comment-removal)

[Français](https://repomix.com/fr/guide/comment-removal)

[Italiano](https://repomix.com/it/guide/comment-removal)

[हिन्दी](https://repomix.com/hi/guide/comment-removal)

[Indonesia](https://repomix.com/id/guide/comment-removal)

[Tiếng Việt](https://repomix.com/vi/guide/comment-removal)

[Русский](https://repomix.com/ru/guide/comment-removal)

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

- [Usage](https://repomix.com/guide/comment-removal#usage "Usage")
- [Supported Languages](https://repomix.com/guide/comment-removal#supported-languages "Supported Languages")
- [Example](https://repomix.com/guide/comment-removal#example "Example")
- [Notes](https://repomix.com/guide/comment-removal#notes "Notes")

# Comment Removal [​](https://repomix.com/guide/comment-removal\#comment-removal)

Repomix can automatically remove comments from your codebase when generating the output file. This can help reduce noise and focus on the actual code.

## Usage [​](https://repomix.com/guide/comment-removal\#usage)

To enable comment removal, set the `removeComments` option to `true` in your `repomix.config.json`:

json

```
{
  "output": {
    "removeComments": true
  }
}
```

## Supported Languages [​](https://repomix.com/guide/comment-removal\#supported-languages)

Repomix supports comment removal for a wide range of programming languages, including:

- JavaScript/TypeScript (`//`, `/* */`)
- Python (`#`, `"""`, `'''`)
- Java (`//`, `/* */`)
- C/C++ (`//`, `/* */`)
- HTML (`<!-- -->`)
- CSS (`/* */`)
- And many more...

## Example [​](https://repomix.com/guide/comment-removal\#example)

Given the following JavaScript code:

javascript

```
// This is a single-line comment
function test() {
  /* This is a
     multi-line comment */
  return true;
}
```

With comment removal enabled, the output will be:

javascript

```
function test() {
  return true;
}
```

## Notes [​](https://repomix.com/guide/comment-removal\#notes)

- Comment removal is performed before other processing steps, such as line number addition.
- Some comments, such as JSDoc comments, may be preserved depending on the language and context.

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageGitHub Repository Processing](https://repomix.com/guide/remote-repository-processing)

[Next pageCode Compression](https://repomix.com/guide/code-compress)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada