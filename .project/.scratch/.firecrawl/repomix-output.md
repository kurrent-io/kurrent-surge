[Skip to content](https://repomix.com/guide/output#VPContent)

[![](https://repomix.com/images/repomix-logo.svg)Repomix](https://repomix.com/)

Search`` `K`

Main Navigation [Guide](https://repomix.com/guide/) [Chrome Extension](https://chromewebstore.google.com/detail/repomix/fimfamikepjgchehkohedilpdigcpkoa) [Join Discord](https://discord.gg/wNYzTwZFku)

English

[简体中文](https://repomix.com/zh-cn/guide/output)

[繁體中文](https://repomix.com/zh-tw/guide/output)

[日本語](https://repomix.com/ja/guide/output)

[Español](https://repomix.com/es/guide/output)

[Português](https://repomix.com/pt-br/guide/output)

[한국어](https://repomix.com/ko/guide/output)

[Deutsch](https://repomix.com/de/guide/output)

[Français](https://repomix.com/fr/guide/output)

[Italiano](https://repomix.com/it/guide/output)

[हिन्दी](https://repomix.com/hi/guide/output)

[Indonesia](https://repomix.com/id/guide/output)

[Tiếng Việt](https://repomix.com/vi/guide/output)

[Русский](https://repomix.com/ru/guide/output)

[x](https://x.com/repomix_ai)[discord](https://discord.gg/wNYzTwZFku)[github](https://github.com/yamadashy/repomix)

English

[简体中文](https://repomix.com/zh-cn/guide/output)

[繁體中文](https://repomix.com/zh-tw/guide/output)

[日本語](https://repomix.com/ja/guide/output)

[Español](https://repomix.com/es/guide/output)

[Português](https://repomix.com/pt-br/guide/output)

[한국어](https://repomix.com/ko/guide/output)

[Deutsch](https://repomix.com/de/guide/output)

[Français](https://repomix.com/fr/guide/output)

[Italiano](https://repomix.com/it/guide/output)

[हिन्दी](https://repomix.com/hi/guide/output)

[Indonesia](https://repomix.com/id/guide/output)

[Tiếng Việt](https://repomix.com/vi/guide/output)

[Русский](https://repomix.com/ru/guide/output)

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

- [XML Format](https://repomix.com/guide/output#xml-format "XML Format")
  - [Why XML as Default Format?](https://repomix.com/guide/output#why-xml-as-default-format "Why XML as Default Format?")
- [Markdown Format](https://repomix.com/guide/output#markdown-format "Markdown Format")
- [JSON Format](https://repomix.com/guide/output#json-format "JSON Format")
  - [Benefits of JSON Format](https://repomix.com/guide/output#benefits-of-json-format "Benefits of JSON Format")
  - [Working with JSON Output Using jq](https://repomix.com/guide/output#working-with-json-output-using-jq "Working with JSON Output Using jq")
- [Plain Text Format](https://repomix.com/guide/output#plain-text-format "Plain Text Format")
- [Usage with AI Models](https://repomix.com/guide/output#usage-with-ai-models "Usage with AI Models")
- [Customization](https://repomix.com/guide/output#customization "Customization")

# Output Formats [​](https://repomix.com/guide/output\#output-formats)

Repomix supports four output formats:

- XML (default)
- Markdown
- JSON
- Plain Text

## XML Format [​](https://repomix.com/guide/output\#xml-format)

bash

```
repomix --style xml
```

XML format is optimized for AI processing:

xml

```
This file is a merged representation of the entire codebase...

<file_summary>
(Metadata and AI instructions)
</file_summary>

<directory_structure>
src/
  index.ts
  utils/
    helper.ts
</directory_structure>

<files>
<file path="src/index.ts">
// File contents here
</file>
</files>

<git_logs>
2025-08-20 00:47:19 +0900|feat(cli): Add --include-logs option for git commit history
README.md
src/cli/cliRun.ts
src/core/git/gitCommand.ts
src/core/git/gitLogHandle.ts
src/core/output/outputGenerate.ts

2025-08-21 00:09:43 +0900|Merge pull request #795 from yamadashy/chore/ratchet-update-ci
.github/workflows/ratchet-update.yml
</git_logs>
```

### Why XML as Default Format? [​](https://repomix.com/guide/output\#why-xml-as-default-format)

Repomix uses XML as the default output format based on extensive research and testing. This decision is grounded in both empirical evidence and practical considerations for AI-assisted code analysis.

Our choice of XML is primarily influenced by official recommendations from major AI providers:

- **Anthropic (Claude)**: Explicitly recommends XML tags for structuring prompts, stating that "Claude was exposed to such prompts during training" ( [source](https://docs.anthropic.com/en/docs/build-with-claude/prompt-engineering/use-xml-tags))
- **Google (Gemini)**: Recommends structured formats including XML for complex tasks ( [documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/learn/prompts/structure-prompts))
- **OpenAI (GPT)**: Advocates for structured prompting in complex scenarios ( [announcement](https://x.com/OpenAIDevs/status/1890147300493914437), [cookbook](https://cookbook.openai.com/examples/gpt-5/gpt-5_prompting_guide))

## Markdown Format [​](https://repomix.com/guide/output\#markdown-format)

bash

```
repomix --style markdown
```

Markdown provides readable formatting:

markdown

````
This file is a merged representation of the entire codebase...

# File Summary
(Metadata and AI instructions)

# Directory Structure
```
src/
index.ts
utils/
helper.ts
```

# Files

## File: src/index.ts
```typescript
// File contents here
```

# Git Logs
```
2025-08-20 00:47:19 +0900|feat(cli): Add --include-logs option for git commit history
README.md
src/cli/cliRun.ts
src/core/git/gitCommand.ts
src/core/git/gitLogHandle.ts
src/core/output/outputGenerate.ts

2025-08-21 00:09:43 +0900|Merge pull request #795 from yamadashy/chore/ratchet-update-ci
.github/workflows/ratchet-update.yml
```
````

## JSON Format [​](https://repomix.com/guide/output\#json-format)

bash

```
repomix --style json
```

JSON format provides structured, programmatically accessible output with camelCase property names:

json

```
{
  "fileSummary": {
    "generationHeader": "This file is a merged representation of the entire codebase, combined into a single document by Repomix.",
    "purpose": "This file contains a packed representation of the entire repository's contents...",
    "fileFormat": "The content is organized as follows...",
    "usageGuidelines": "- This file should be treated as read-only...",
    "notes": "- Some files may have been excluded based on .gitignore rules..."
  },
  "userProvidedHeader": "Custom header text if specified",
  "directoryStructure": "src/
  cli/
    cliOutput.ts
    index.ts
  config/
    configLoader.ts",
  "files": {
    "src/index.js": "// File contents here",
    "src/utils.js": "// File contents here"
  },
  "instruction": "Custom instructions from instructionFilePath"
}
```

### Benefits of JSON Format [​](https://repomix.com/guide/output\#benefits-of-json-format)

The JSON format is ideal for:

- **Programmatic processing**: Easy to parse and manipulate with JSON libraries in any programming language
- **API integration**: Direct consumption by web services and applications
- **AI tool compatibility**: Structured format optimized for machine learning and AI systems
- **Data analysis**: Straightforward extraction of specific information using tools like `jq`

### Working with JSON Output Using `jq` [​](https://repomix.com/guide/output\#working-with-json-output-using-jq)

The JSON format makes it easy to extract specific information programmatically. Here are common examples:

#### Basic File Operations [​](https://repomix.com/guide/output\#basic-file-operations)

bash

```
# List all file paths
cat repomix-output.json | jq -r '.files | keys[]'

# Count total number of files
cat repomix-output.json | jq '.files | keys | length'

# Extract specific file content
cat repomix-output.json | jq -r '.files["README.md"]'
cat repomix-output.json | jq -r '.files["src/index.js"]'
```

#### File Filtering and Analysis [​](https://repomix.com/guide/output\#file-filtering-and-analysis)

bash

```
# Find files by extension
cat repomix-output.json | jq -r '.files | keys[] | select(endswith(".ts"))'
cat repomix-output.json | jq -r '.files | keys[] | select(endswith(".js") or endswith(".ts"))'

# Get files containing specific text
cat repomix-output.json | jq -r '.files | to_entries[] | select(.value | contains("function")) | .key'

# Create a file list with character counts
cat repomix-output.json | jq -r '.files | to_entries[] | "\(.key): \(.value | length) characters"'
```

#### Metadata Extraction [​](https://repomix.com/guide/output\#metadata-extraction)

bash

```
# Extract directory structure
cat repomix-output.json | jq -r '.directoryStructure'

# Get file summary information
cat repomix-output.json | jq '.fileSummary.purpose'
cat repomix-output.json | jq -r '.fileSummary.generationHeader'

# Extract user-provided header (if exists)
cat repomix-output.json | jq -r '.userProvidedHeader // "No header provided"'

# Get custom instructions
cat repomix-output.json | jq -r '.instruction // "No instructions provided"'
```

#### Advanced Analysis [​](https://repomix.com/guide/output\#advanced-analysis)

bash

```
# Find largest files by content length
cat repomix-output.json | jq -r '.files | to_entries[] | [.key, (.value | length)] | @tsv' | sort -k2 -nr | head -10

# Search for files containing specific patterns
cat repomix-output.json | jq -r '.files | to_entries[] | select(.value | test("import.*react"; "i")) | .key'

# Extract file paths matching multiple extensions
cat repomix-output.json | jq -r '.files | keys[] | select(test("\.(js|ts|jsx|tsx)$"))'
```

## Plain Text Format [​](https://repomix.com/guide/output\#plain-text-format)

bash

```
repomix --style plain
```

Output structure:

text

```
This file is a merged representation of the entire codebase...

================
File Summary
================
(Metadata and AI instructions)

================
Directory Structure
================
src/
  index.ts
  utils/
    helper.ts

================
Files
================

================
File: src/index.ts
================
// File contents here

================
Git Logs
================
2025-08-20 00:47:19 +0900|feat(cli): Add --include-logs option for git commit history
README.md
src/cli/cliRun.ts
src/core/git/gitCommand.ts
src/core/git/gitLogHandle.ts
src/core/output/outputGenerate.ts

2025-08-21 00:09:43 +0900|Merge pull request #795 from yamadashy/chore/ratchet-update-ci
.github/workflows/ratchet-update.yml
```

## Usage with AI Models [​](https://repomix.com/guide/output\#usage-with-ai-models)

Each format works well with AI models, but consider:

- Use XML for Claude (best parsing accuracy)
- Use Markdown for general readability
- Use JSON for programmatic processing and API integration
- Use Plain Text for simplicity and universal compatibility

## Customization [​](https://repomix.com/guide/output\#customization)

Set default format in `repomix.config.json`:

json

```
{
  "output": {
    "style": "xml",
    "filePath": "output.xml"
  }
}
```

Last updated: 2/17/26, 10:21 AM

Pager

[Previous pageUse Cases](https://repomix.com/guide/use-cases)

[Next pageCommand Line Options](https://repomix.com/guide/command-line-options)

Released under the MIT License.

Copyright © 2024 Kazuki Yamada