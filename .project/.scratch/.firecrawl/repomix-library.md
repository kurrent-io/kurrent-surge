[Skip to content](https://repomix.com/guide/development/using-repomix-as-a-library#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/development/using-repomix-as-a-library)

[繁體中文](https://repomix.com/zh-tw/guide/development/using-repomix-as-a-library)

[日本語](https://repomix.com/ja/guide/development/using-repomix-as-a-library)

[Español](https://repomix.com/es/guide/development/using-repomix-as-a-library)

[Português](https://repomix.com/pt-br/guide/development/using-repomix-as-a-library)

[한국어](https://repomix.com/ko/guide/development/using-repomix-as-a-library)

[Deutsch](https://repomix.com/de/guide/development/using-repomix-as-a-library)

[Français](https://repomix.com/fr/guide/development/using-repomix-as-a-library)

[Italiano](https://repomix.com/it/guide/development/using-repomix-as-a-library)

[हिन्दी](https://repomix.com/hi/guide/development/using-repomix-as-a-library)

[Indonesia](https://repomix.com/id/guide/development/using-repomix-as-a-library)

[Tiếng Việt](https://repomix.com/vi/guide/development/using-repomix-as-a-library)

[Русский](https://repomix.com/ru/guide/development/using-repomix-as-a-library)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/development/using-repomix-as-a-library)

[繁體中文](https://repomix.com/zh-tw/guide/development/using-repomix-as-a-library)

[日本語](https://repomix.com/ja/guide/development/using-repomix-as-a-library)

[Español](https://repomix.com/es/guide/development/using-repomix-as-a-library)

[Português](https://repomix.com/pt-br/guide/development/using-repomix-as-a-library)

[한국어](https://repomix.com/ko/guide/development/using-repomix-as-a-library)

[Deutsch](https://repomix.com/de/guide/development/using-repomix-as-a-library)

[Français](https://repomix.com/fr/guide/development/using-repomix-as-a-library)

[Italiano](https://repomix.com/it/guide/development/using-repomix-as-a-library)

[हिन्दी](https://repomix.com/hi/guide/development/using-repomix-as-a-library)

[Indonesia](https://repomix.com/id/guide/development/using-repomix-as-a-library)

[Tiếng Việt](https://repomix.com/vi/guide/development/using-repomix-as-a-library)

[Русский](https://repomix.com/ru/guide/development/using-repomix-as-a-library)

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

- [Installation](https://repomix.com/guide/development/using-repomix-as-a-library#installation "Installation")
- [Basic Usage](https://repomix.com/guide/development/using-repomix-as-a-library#basic-usage "Basic Usage")
- [Processing Remote Repositories](https://repomix.com/guide/development/using-repomix-as-a-library#processing-remote-repositories "Processing Remote Repositories")
- [Using Core Components](https://repomix.com/guide/development/using-repomix-as-a-library#using-core-components "Using Core Components")
- [Bundling](https://repomix.com/guide/development/using-repomix-as-a-library#bundling "Bundling")
- [Real-World Example](https://repomix.com/guide/development/using-repomix-as-a-library#real-world-example "Real-World Example")

# Using Repomix as a Library [​](https://repomix.com/guide/development/using-repomix-as-a-library\#using-repomix-as-a-library)

In addition to using Repomix as a CLI tool, you can integrate its functionality directly into your Node.js applications.

## Installation [​](https://repomix.com/guide/development/using-repomix-as-a-library\#installation)

Install Repomix as a dependency in your project:

bash

```
npm install repomix
```

## Basic Usage [​](https://repomix.com/guide/development/using-repomix-as-a-library\#basic-usage)

The simplest way to use Repomix is through the `runCli` function, which provides the same functionality as the command-line interface:

javascript

```
import { runCli, type CliOptions } from 'repomix';

// Process current directory with custom options
async function packProject() {
  const options = {
    output: 'output.xml',
    style: 'xml',
    compress: true,
    quiet: true
  } as CliOptions;

  const result = await runCli(['.'], process.cwd(), options);
  return result.packResult;
}
```

The `result.packResult` contains information about the processed files, including:

- `totalFiles`: Number of files processed
- `totalCharacters`: Total character count
- `totalTokens`: Total token count (useful for LLM context limits)
- `fileCharCounts`: Character counts per file
- `fileTokenCounts`: Token counts per file

## Processing Remote Repositories [​](https://repomix.com/guide/development/using-repomix-as-a-library\#processing-remote-repositories)

You can clone and process a remote repository:

javascript

```
import { runCli, type CliOptions } from 'repomix';

// Clone and process a GitHub repo
async function processRemoteRepo(repoUrl) {
  const options = {
    remote: repoUrl,
    output: 'output.xml',
    compress: true
  } as CliOptions;

  return await runCli(['.'], process.cwd(), options);
}
```

## Using Core Components [​](https://repomix.com/guide/development/using-repomix-as-a-library\#using-core-components)

For more control, you can use Repomix's low-level APIs directly:

javascript

```
import { searchFiles, collectFiles, processFiles, TokenCounter } from 'repomix';

async function analyzeFiles(directory) {
  // Find and collect files
  const { filePaths } = await searchFiles(directory, { /* config */ });
  const rawFiles = await collectFiles(filePaths, directory);
  const processedFiles = await processFiles(rawFiles, { /* config */ });

  // Count tokens
  const tokenCounter = new TokenCounter('o200k_base');

  // Return analysis results
  return processedFiles.map(file => ({
    path: file.path,
    tokens: tokenCounter.countTokens(file.content)
  }));
}
```

## Bundling [​](https://repomix.com/guide/development/using-repomix-as-a-library\#bundling)

When bundling repomix with tools like Rolldown or esbuild, some dependencies must remain external and WASM files need to be copied:

**External dependencies (cannot be bundled):**

- `tinypool` \- Spawns worker threads using file paths
- `tiktoken` \- Loads WASM files dynamically at runtime

**WASM files to copy:**

- `web-tree-sitter.wasm` → Same directory as bundled JS (required for code compression feature)
- Tree-sitter language files → Directory specified by `REPOMIX_WASM_DIR` environment variable

For a working example, see [website/server/scripts/bundle.mjs](https://github.com/yamadashy/repomix/blob/main/website/server/scripts/bundle.mjs).

## Real-World Example [​](https://repomix.com/guide/development/using-repomix-as-a-library\#real-world-example)

The Repomix website ( [repomix.com](https://repomix.com/)) uses Repomix as a library to process remote repositories. You can see the implementation in [website/server/src/remoteRepo.ts](https://github.com/yamadashy/repomix/blob/main/website/server/src/remoteRepo.ts).

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageGitHub Actions](https://repomix.com/guide/github-actions)

[Next pageAI-Assisted Development Tips](https://repomix.com/guide/tips/best-practices)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada