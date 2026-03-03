[Skip to content](https://repomix.com/guide/tips/best-practices#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/tips/best-practices)

[繁體中文](https://repomix.com/zh-tw/guide/tips/best-practices)

[日本語](https://repomix.com/ja/guide/tips/best-practices)

[Español](https://repomix.com/es/guide/tips/best-practices)

[Português](https://repomix.com/pt-br/guide/tips/best-practices)

[한국어](https://repomix.com/ko/guide/tips/best-practices)

[Deutsch](https://repomix.com/de/guide/tips/best-practices)

[Français](https://repomix.com/fr/guide/tips/best-practices)

[Italiano](https://repomix.com/it/guide/tips/best-practices)

[हिन्दी](https://repomix.com/hi/guide/tips/best-practices)

[Indonesia](https://repomix.com/id/guide/tips/best-practices)

[Tiếng Việt](https://repomix.com/vi/guide/tips/best-practices)

[Русский](https://repomix.com/ru/guide/tips/best-practices)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/tips/best-practices)

[繁體中文](https://repomix.com/zh-tw/guide/tips/best-practices)

[日本語](https://repomix.com/ja/guide/tips/best-practices)

[Español](https://repomix.com/es/guide/tips/best-practices)

[Português](https://repomix.com/pt-br/guide/tips/best-practices)

[한국어](https://repomix.com/ko/guide/tips/best-practices)

[Deutsch](https://repomix.com/de/guide/tips/best-practices)

[Français](https://repomix.com/fr/guide/tips/best-practices)

[Italiano](https://repomix.com/it/guide/tips/best-practices)

[हिन्दी](https://repomix.com/hi/guide/tips/best-practices)

[Indonesia](https://repomix.com/id/guide/tips/best-practices)

[Tiếng Việt](https://repomix.com/vi/guide/tips/best-practices)

[Русский](https://repomix.com/ru/guide/tips/best-practices)

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

- [Basic Development Approach](https://repomix.com/guide/tips/best-practices#basic-development-approach "Basic Development Approach")
  - [The Power of Existing Code](https://repomix.com/guide/tips/best-practices#the-power-of-existing-code "The Power of Existing Code")
- [The Modular Approach](https://repomix.com/guide/tips/best-practices#the-modular-approach "The Modular Approach")
- [Ensuring Quality Through Testing](https://repomix.com/guide/tips/best-practices#ensuring-quality-through-testing "Ensuring Quality Through Testing")
- [Balancing Planning and Implementation](https://repomix.com/guide/tips/best-practices#balancing-planning-and-implementation "Balancing Planning and Implementation")
- [Conclusion](https://repomix.com/guide/tips/best-practices#conclusion "Conclusion")

# AI-Assisted Development Best Practices: From My Experience [​](https://repomix.com/guide/tips/best-practices\#ai-assisted-development-best-practices-from-my-experience)

While I haven't successfully completed a large-scale project using AI yet, I'd like to share what I've learned so far from my experience working with AI in development.

## Basic Development Approach [​](https://repomix.com/guide/tips/best-practices\#basic-development-approach)

When working with AI, attempting to implement all features at once can lead to unexpected issues and project stagnation. That's why it's more effective to start with core functionality and build each feature one at a time, ensuring solid implementation before moving forward.

### The Power of Existing Code [​](https://repomix.com/guide/tips/best-practices\#the-power-of-existing-code)

This approach is effective because implementing core functionality allows you to materialize your ideal design and coding style through actual code. The most effective way to communicate your project vision is through code that reflects your standards and preferences.

By starting with core features and ensuring each component works properly before moving on, the entire project maintains consistency, making it easier for AI to generate more appropriate code.

## The Modular Approach [​](https://repomix.com/guide/tips/best-practices\#the-modular-approach)

Breaking code into smaller modules is crucial. In my experience, keeping files around 250 lines of code makes it easier to give clear instructions to AI and makes the trial-and-error process more efficient. While token count would be a more accurate metric, line count is more practical for human developers to work with, so we use that as a guideline.

This modularization isn't just about separating frontend, backend, and database components - it's about breaking down functionality at a much finer level. For example, within a single feature, you might separate validation, error handling, and other specific functionalities into distinct modules. Of course, high-level separation is also important, and implementing this modular approach gradually helps maintain clear instructions and enables AI to generate more appropriate code. This approach is effective not just for AI but for human developers as well.

## Ensuring Quality Through Testing [​](https://repomix.com/guide/tips/best-practices\#ensuring-quality-through-testing)

I consider testing to be crucial in AI-assisted development. Tests serve not only as quality assurance measures but also as documentation that clearly demonstrates code intentions. When asking AI to implement new features, existing test code effectively acts as a specification document.

Tests are also an excellent tool for validating the correctness of AI-generated code. For instance, when having AI implement new functionality for a module, writing test cases beforehand allows you to objectively evaluate whether the generated code behaves as expected. This aligns well with Test-Driven Development (TDD) principles and is particularly effective when collaborating with AI.

## Balancing Planning and Implementation [​](https://repomix.com/guide/tips/best-practices\#balancing-planning-and-implementation)

Before implementing large-scale features, I recommend first discussing the plan with AI. Organizing requirements and considering architecture leads to smoother implementation. A good practice is to compile requirements first, then move to a separate chat session for implementation work.

It's essential to have human review of AI output and make adjustments as needed. While the quality of AI-generated code is generally moderate, it still accelerates development compared to writing everything from scratch.

## Conclusion [​](https://repomix.com/guide/tips/best-practices\#conclusion)

By following these practices, you can leverage AI's strengths while building a consistent, high-quality codebase. Even as your project grows in size, each component remains well-defined and manageable.

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageUsing Repomix as a Library](https://repomix.com/guide/development/using-repomix-as-a-library)

[Next pageCommunity Projects](https://repomix.com/guide/community-projects)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada