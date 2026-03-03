[Skip to content](https://repomix.com/guide/installation#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/installation)

[繁體中文](https://repomix.com/zh-tw/guide/installation)

[日本語](https://repomix.com/ja/guide/installation)

[Español](https://repomix.com/es/guide/installation)

[Português](https://repomix.com/pt-br/guide/installation)

[한국어](https://repomix.com/ko/guide/installation)

[Deutsch](https://repomix.com/de/guide/installation)

[Français](https://repomix.com/fr/guide/installation)

[Italiano](https://repomix.com/it/guide/installation)

[हिन्दी](https://repomix.com/hi/guide/installation)

[Indonesia](https://repomix.com/id/guide/installation)

[Tiếng Việt](https://repomix.com/vi/guide/installation)

[Русский](https://repomix.com/ru/guide/installation)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/installation)

[繁體中文](https://repomix.com/zh-tw/guide/installation)

[日本語](https://repomix.com/ja/guide/installation)

[Español](https://repomix.com/es/guide/installation)

[Português](https://repomix.com/pt-br/guide/installation)

[한국어](https://repomix.com/ko/guide/installation)

[Deutsch](https://repomix.com/de/guide/installation)

[Français](https://repomix.com/fr/guide/installation)

[Italiano](https://repomix.com/it/guide/installation)

[हिन्दी](https://repomix.com/hi/guide/installation)

[Indonesia](https://repomix.com/id/guide/installation)

[Tiếng Việt](https://repomix.com/vi/guide/installation)

[Русский](https://repomix.com/ru/guide/installation)

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

- [Using npx (No Installation Required)](https://repomix.com/guide/installation#using-npx-no-installation-required "Using npx (No Installation Required)")
- [Global Installation](https://repomix.com/guide/installation#global-installation "Global Installation")
- [Docker Installation](https://repomix.com/guide/installation#docker-installation "Docker Installation")
- [VSCode Extension](https://repomix.com/guide/installation#vscode-extension "VSCode Extension")
- [Browser Extension](https://repomix.com/guide/installation#browser-extension "Browser Extension")
  - [Install](https://repomix.com/guide/installation#install "Install")
  - [Features](https://repomix.com/guide/installation#features "Features")
- [System Requirements](https://repomix.com/guide/installation#system-requirements "System Requirements")
- [Verification](https://repomix.com/guide/installation#verification "Verification")

# Installation [​](https://repomix.com/guide/installation\#installation)

## Using npx (No Installation Required) [​](https://repomix.com/guide/installation\#using-npx-no-installation-required)

bash

```
npx repomix@latest
```

## Global Installation [​](https://repomix.com/guide/installation\#global-installation)

npmyarnpnpmbunHomebrew

bash

```
npm install -g repomix
```

bash

```
yarn global add repomix
```

bash

```
pnpm add -g repomix
```

bash

```
bun add -g repomix
```

bash

```
brew install repomix
```

## Docker Installation [​](https://repomix.com/guide/installation\#docker-installation)

Pull and run the Docker image:

bash

```
# Current directory
docker run -v .:/app -it --rm ghcr.io/yamadashy/repomix

# Specific directory
docker run -v .:/app -it --rm ghcr.io/yamadashy/repomix path/to/directory

# Remote repository
docker run -v ./output:/app -it --rm ghcr.io/yamadashy/repomix --remote yamadashy/repomix
```

## VSCode Extension [​](https://repomix.com/guide/installation\#vscode-extension)

Run Repomix directly in VSCode with the community-maintained [Repomix Runner](https://marketplace.visualstudio.com/items?itemName=DorianMassoulier.repomix-runner) extension.

Features:

- Pack any folder with just a few clicks
- Choose between file or content mode for copying
- Automatic cleanup of output files
- Works with repomix.config.json

Install it from the [VSCode Marketplace](https://marketplace.visualstudio.com/items?itemName=DorianMassoulier.repomix-runner).

## Browser Extension [​](https://repomix.com/guide/installation\#browser-extension)

Get instant access to Repomix directly from any GitHub repository! Our Chrome extension adds a convenient "Repomix" button to GitHub repository pages.

![Repomix Browser Extension](https://repomix.com/images/docs/browser-extension.png)

### Install [​](https://repomix.com/guide/installation\#install)

- Chrome Extension: [Repomix - Chrome Web Store](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa)
- Firefox Add-on: [Repomix - Firefox Add-ons](https://addons.mozilla.org/firefox/addon/repomix/)

### Features [​](https://repomix.com/guide/installation\#features)

- One-click access to Repomix for any GitHub repository
- More exciting features coming soon!

## System Requirements [​](https://repomix.com/guide/installation\#system-requirements)

- Node.js: ≥ 20.0.0
- Git: Required for remote repository processing

## Verification [​](https://repomix.com/guide/installation\#verification)

After installation, verify that Repomix is working:

bash

```
repomix --version
repomix --help
```

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageGetting Started](https://repomix.com/guide/)

[Next pageBasic Usage](https://repomix.com/guide/usage)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada