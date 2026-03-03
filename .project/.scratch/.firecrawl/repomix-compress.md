[Skip to content](https://repomix.com/guide/code-compress#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/code-compress)

[繁體中文](https://repomix.com/zh-tw/guide/code-compress)

[日本語](https://repomix.com/ja/guide/code-compress)

[Español](https://repomix.com/es/guide/code-compress)

[Português](https://repomix.com/pt-br/guide/code-compress)

[한국어](https://repomix.com/ko/guide/code-compress)

[Deutsch](https://repomix.com/de/guide/code-compress)

[Français](https://repomix.com/fr/guide/code-compress)

[Italiano](https://repomix.com/it/guide/code-compress)

[हिन्दी](https://repomix.com/hi/guide/code-compress)

[Indonesia](https://repomix.com/id/guide/code-compress)

[Tiếng Việt](https://repomix.com/vi/guide/code-compress)

[Русский](https://repomix.com/ru/guide/code-compress)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/code-compress)

[繁體中文](https://repomix.com/zh-tw/guide/code-compress)

[日本語](https://repomix.com/ja/guide/code-compress)

[Español](https://repomix.com/es/guide/code-compress)

[Português](https://repomix.com/pt-br/guide/code-compress)

[한국어](https://repomix.com/ko/guide/code-compress)

[Deutsch](https://repomix.com/de/guide/code-compress)

[Français](https://repomix.com/fr/guide/code-compress)

[Italiano](https://repomix.com/it/guide/code-compress)

[हिन्दी](https://repomix.com/hi/guide/code-compress)

[Indonesia](https://repomix.com/id/guide/code-compress)

[Tiếng Việt](https://repomix.com/vi/guide/code-compress)

[Русский](https://repomix.com/ru/guide/code-compress)

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

- [Basic Usage](https://repomix.com/guide/code-compress#basic-usage "Basic Usage")
- [How It Works](https://repomix.com/guide/code-compress#how-it-works "How It Works")
  - [Example](https://repomix.com/guide/code-compress#example "Example")
- [Configuration](https://repomix.com/guide/code-compress#configuration "Configuration")
- [Use Cases](https://repomix.com/guide/code-compress#use-cases "Use Cases")
- [Related Options](https://repomix.com/guide/code-compress#related-options "Related Options")

# Code Compression [​](https://repomix.com/guide/code-compress\#code-compression)

Code compression is a powerful feature that intelligently extracts essential code structures while removing implementation details. This is particularly useful for reducing token count while maintaining important structural information about your codebase.

NOTE

This is an experimental feature that we'll be actively improving based on user feedback and real-world usage

## Basic Usage [​](https://repomix.com/guide/code-compress\#basic-usage)

Enable code compression using the `--compress` flag:

bash

```
repomix --compress
```

You can also use it with remote repositories:

bash

```
repomix --remote user/repo --compress
```

## How It Works [​](https://repomix.com/guide/code-compress\#how-it-works)

The compression algorithm processes code using tree-sitter parsing to extract and preserve essential structural elements while removing implementation details.

The compression preserves:

- Function and method signatures
- Interface and type definitions
- Class structures and properties
- Important structural elements

While removing:

- Function and method implementations
- Loop and conditional logic details
- Internal variable declarations
- Implementation-specific code

### Example [​](https://repomix.com/guide/code-compress\#example)

Original TypeScript code:

typescript

```
import { ShoppingItem } from './shopping-item';

/**
 * Calculate the total price of shopping items
 */
const calculateTotal = (
  items: ShoppingItem[]
) => {
  let total = 0;
  for (const item of items) {
    total += item.price * item.quantity;
  }
  return total;
}

// Shopping item interface
interface Item {
  name: string;
  price: number;
  quantity: number;
}
```

After compression:

typescript

```
import { ShoppingItem } from './shopping-item';
⋮----
/**
 * Calculate the total price of shopping items
 */
const calculateTotal = (
  items: ShoppingItem[]
) => {
⋮----
// Shopping item interface
interface Item {
  name: string;
  price: number;
  quantity: number;
}
```

## Configuration [​](https://repomix.com/guide/code-compress\#configuration)

You can enable compression in your configuration file:

json

```
{
  "output": {
    "compress": true
  }
}
```

## Use Cases [​](https://repomix.com/guide/code-compress\#use-cases)

Code compression is particularly useful when:

- Analyzing code structure and architecture
- Reducing token count for LLM processing
- Creating high-level documentation
- Understanding code patterns and signatures
- Sharing API and interface designs

## Related Options [​](https://repomix.com/guide/code-compress\#related-options)

You can combine compression with other options:

- `--remove-comments`: Remove code comments
- `--remove-empty-lines`: Remove empty lines
- `--output-show-line-numbers`: Add line numbers to output

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageComment Removal](https://repomix.com/guide/comment-removal)

[Next pageSecurity](https://repomix.com/guide/security)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada