# Repomix Output Formats Guide

Complete guide to choosing and working with Repomix's four output formats.

## Overview

Repomix supports four output formats, each optimized for different use cases:

| Format | Extension | Best For | Parsing | Size |
|--------|-----------|----------|---------|------|
| XML | `.xml` | AI analysis, structured data | Easy | Medium |
| Markdown | `.md` | Human reading, documentation | Medium | Large |
| JSON | `.json` | Programmatic processing | Easiest | Small |
| Plain | `.txt` | Simple viewing, grep | Hardest | Medium |

## XML Format (Default)

### Structure

```xml
<?xml version="1.0" encoding="UTF-8"?>
<repomix>
  <metadata>
    <generatedAt>2024-01-15T10:30:00Z</generatedAt>
    <repositoryPath>/path/to/project</repositoryPath>
  </metadata>

  <metrics>
    <totalFiles>150</totalFiles>
    <totalLines>12000</totalLines>
    <totalTokens>35000</totalTokens>
    <topFiles>
      <file>
        <path>src/main.ts</path>
        <lines>450</lines>
        <tokens>1200</tokens>
      </file>
    </topFiles>
  </metrics>

  <directoryTree>
    <![CDATA[
    src/
    ├── components/
    │   ├── Button.tsx
    │   └── Input.tsx
    └── utils/
        └── helpers.ts
    ]]>
  </directoryTree>

  <files>
    <file>
      <path>src/main.ts</path>
      <content><![CDATA[
        // File content here
      ]]></content>
    </file>
  </files>
</repomix>
```

### Usage

```bash
# Generate XML (default)
repomix

# Explicit XML
repomix --style xml -o output.xml
```

**MCP:**
```typescript
await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'xml'
});
```

### Advantages

**For AI Analysis:**
- Clear semantic structure
- Easy to parse sections
- CDATA sections for code content
- Metadata readily accessible

**For Processing:**
- Standard XML parsers available
- XPath queries possible
- Schema validation available
- Hierarchical structure

### Use Cases

- Claude/ChatGPT analysis
- Automated code review
- CI/CD integration
- Structured data extraction
- API integrations

### Parsing Example

```typescript
import { parseString } from 'xml2js';

parseString(xmlContent, (err, result) => {
  const metrics = result.repomix.metrics[0];
  console.log(`Total files: ${metrics.totalFiles}`);
  console.log(`Total tokens: ${metrics.totalTokens}`);

  const files = result.repomix.files[0].file;
  files.forEach(file => {
    console.log(`${file.path}: ${file.content}`);
  });
});
```

## Markdown Format

### Structure

```markdown
# Repository Analysis

**Generated:** 2024-01-15T10:30:00Z
**Path:** /path/to/project

## Metrics

- Total Files: 150
- Total Lines: 12,000
- Total Tokens: 35,000

### Largest Files

1. `src/main.ts` - 450 lines (1,200 tokens)
2. `src/app.ts` - 380 lines (1,050 tokens)

## Directory Structure

\`\`\`
src/
├── components/
│   ├── Button.tsx
│   └── Input.tsx
└── utils/
    └── helpers.ts
\`\`\`

## Files

### src/main.ts

\`\`\`typescript
// File content here
\`\`\`

### src/app.ts

\`\`\`typescript
// File content here
\`\`\`
```

### Usage

```bash
# Generate Markdown
repomix --style markdown -o output.md
```

**MCP:**
```typescript
await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'markdown'
});
```

### Advantages

**For Humans:**
- Highly readable
- Syntax highlighting in viewers
- Easy navigation with headers
- Good for documentation

**For AI:**
- Natural language structure
- Code blocks clearly delimited
- Headings provide context
- Familiar format for LLMs

### Use Cases

- Code review documents
- Architecture documentation
- GitHub/GitLab wikis
- Blog posts about code
- Educational materials
- README generation

### Rendering

Markdown output renders beautifully in:
- GitHub/GitLab
- VS Code
- Markdown viewers
- Static site generators
- Documentation platforms

## JSON Format

### Structure

```json
{
  "metadata": {
    "generatedAt": "2024-01-15T10:30:00Z",
    "repositoryPath": "/path/to/project"
  },
  "metrics": {
    "totalFiles": 150,
    "totalLines": 12000,
    "totalTokens": 35000,
    "topFiles": [
      {
        "path": "src/main.ts",
        "lines": 450,
        "tokens": 1200
      }
    ]
  },
  "directoryTree": "src/\n├── components/\n│   ├── Button.tsx\n│   └── Input.tsx\n└── utils/\n    └── helpers.ts",
  "files": [
    {
      "path": "src/main.ts",
      "content": "// File content here",
      "lines": 450,
      "tokens": 1200
    }
  ]
}
```

### Usage

```bash
# Generate JSON
repomix --style json -o output.json
```

**MCP:**
```typescript
await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'json'
});
```

### Advantages

**For Programming:**
- Easiest to parse
- Native JavaScript support
- Strongly typed structure
- Minimal overhead

**For Analysis:**
- Query with jq
- Import into databases
- Use in APIs
- Machine learning pipelines

### Use Cases

- Automated analysis pipelines
- Database imports
- API responses
- Data science workflows
- Metrics dashboards
- CI/CD integration

### Parsing Example

```typescript
// TypeScript
const data = JSON.parse(jsonContent);
console.log(`Total files: ${data.metrics.totalFiles}`);
console.log(`Total tokens: ${data.metrics.totalTokens}`);

data.files.forEach(file => {
  console.log(`${file.path}: ${file.lines} lines`);
});
```

```bash
# jq queries
jq '.metrics.totalFiles' output.json
jq '.metrics.topFiles[] | select(.tokens > 1000)' output.json
jq '.files[] | {path, lines}' output.json
```

### JSON Schema

```typescript
interface RepomixOutput {
  metadata: {
    generatedAt: string;
    repositoryPath: string;
  };
  metrics: {
    totalFiles: number;
    totalLines: number;
    totalTokens: number;
    topFiles: Array<{
      path: string;
      lines: number;
      tokens: number;
    }>;
  };
  directoryTree: string;
  files: Array<{
    path: string;
    content: string;
    lines: number;
    tokens: number;
  }>;
}
```

## Plain Text Format

### Structure

```
================================================================================
Repository Analysis
================================================================================

Generated: 2024-01-15T10:30:00Z
Path: /path/to/project

--------------------------------------------------------------------------------
Metrics
--------------------------------------------------------------------------------

Total Files: 150
Total Lines: 12,000
Total Tokens: 35,000

Largest Files:
1. src/main.ts - 450 lines (1,200 tokens)
2. src/app.ts - 380 lines (1,050 tokens)

--------------------------------------------------------------------------------
Directory Structure
--------------------------------------------------------------------------------

src/
├── components/
│   ├── Button.tsx
│   └── Input.tsx
└── utils/
    └── helpers.ts

--------------------------------------------------------------------------------
Files
--------------------------------------------------------------------------------

File: src/main.ts
----------------------------------------

// File content here

File: src/app.ts
----------------------------------------

// File content here
```

### Usage

```bash
# Generate plain text
repomix --style plain -o output.txt
```

**MCP:**
```typescript
await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'plain'
});
```

### Advantages

**For Simplicity:**
- No parsing required
- Works with basic tools
- Universal compatibility
- Minimal formatting

**For Processing:**
- grep/awk/sed friendly
- Terminal viewing
- Log file analysis
- Simple diffing

### Use Cases

- Command-line workflows
- Log file generation
- Simple text processing
- Email attachments
- Legacy system integration

### Processing Example

```bash
# Search for patterns
grep -n "function" output.txt

# Count occurrences
grep -c "import" output.txt

# Extract metrics
awk '/Total Files:/ {print $3}' output.txt

# View in terminal
less output.txt
```

## Format Comparison

### Size Comparison

For same codebase (150 files, 35k tokens):

| Format | Size | Compression |
|--------|------|-------------|
| XML | 2.1 MB | Best with gzip |
| Markdown | 2.5 MB | Good |
| JSON | 1.8 MB | Best raw |
| Plain | 2.0 MB | Good |

### Parsing Difficulty

| Format | Parse Complexity | Tools Required |
|--------|------------------|----------------|
| XML | Medium | XML parser |
| Markdown | Hard | Custom parser |
| JSON | Easy | JSON.parse() |
| Plain | Very Hard | Regex/manual |

### AI Analysis Suitability

| Format | Claude | GPT | Gemini | Structure |
|--------|--------|-----|--------|-----------|
| XML | Excellent | Excellent | Excellent | Best |
| Markdown | Excellent | Excellent | Excellent | Good |
| JSON | Good | Good | Good | Best |
| Plain | Fair | Fair | Fair | Poor |

## Format Selection Guide

### Choose XML When:

- AI analysis is primary use case
- Need structured, parseable format
- Want standard format with tools
- Integrating with XML-based systems
- Need validation and schemas

### Choose Markdown When:

- Human readability is priority
- Generating documentation
- Publishing to GitHub/GitLab
- Creating educational content
- Want syntax highlighting

### Choose JSON When:

- Programmatic processing is goal
- Building automated pipelines
- Need database integration
- Want TypeScript type safety
- Smallest file size needed

### Choose Plain Text When:

- Simplicity is paramount
- Using command-line tools
- No parsing infrastructure available
- Need universal compatibility
- Working in constrained environments

## Format Conversion

### XML to JSON

```bash
# Using xq (like jq for XML)
xq . output.xml > output.json
```

### JSON to Markdown

```typescript
function jsonToMarkdown(data: RepomixOutput): string {
  let md = `# Repository Analysis\n\n`;
  md += `**Generated:** ${data.metadata.generatedAt}\n\n`;
  md += `## Metrics\n\n`;
  md += `- Total Files: ${data.metrics.totalFiles}\n`;
  md += `- Total Tokens: ${data.metrics.totalTokens}\n\n`;

  data.files.forEach(file => {
    md += `### ${file.path}\n\n\`\`\`\n${file.content}\n\`\`\`\n\n`;
  });

  return md;
}
```

### Markdown to HTML

```bash
# Using pandoc
pandoc output.md -o output.html

# With syntax highlighting
pandoc output.md -o output.html --highlight-style=github
```

## Configuration Examples

### XML for CI/CD

```typescript
// repomix.config.ts
import { defineConfig } from 'repomix';

export default defineConfig({
  output: {
    style: 'xml',
    filePath: 'analysis.xml',
    compress: true
  }
});
```

### Markdown for Documentation

```json
{
  "output": {
    "style": "markdown",
    "filePath": "ARCHITECTURE.md",
    "removeComments": false,
    "showLineNumbers": true
  }
}
```

### JSON for Data Pipeline

```typescript
import { defineConfig } from 'repomix';

export default defineConfig({
  output: {
    style: 'json',
    filePath: 'codebase.json',
    compress: true,
    removeComments: true
  }
});
```

## Best Practices

1. **Default to XML** for AI analysis
2. **Use Markdown** for human-consumed documentation
3. **Use JSON** for programmatic workflows
4. **Use Plain** only when necessary
5. **Compress JSON** for storage (gzip)
6. **Validate XML** with schemas when possible
7. **Render Markdown** for presentation
8. **Parse JSON** with TypeScript types
9. **Combine formats** for different audiences
10. **Test format choice** with actual use case

## Format-Specific Options

### Parsable Style

Escape special characters for valid formatting:

```bash
repomix --style xml --parsable-style
```

Ensures XML/Markdown remains valid even with special characters in code.

### Line Numbers

Add line numbers (works best with Markdown):

```bash
repomix --style markdown --output-show-line-numbers
```

### Header Text

Custom header (all formats):

```bash
repomix --style markdown --header-text "Project X Analysis"
```

Or in config:

```json
{
  "output": {
    "style": "markdown",
    "headerText": "Project X Analysis - Confidential"
  }
}
```

## Troubleshooting

### Invalid XML

**Problem:** Special characters break XML

**Solution:**
```bash
repomix --style xml --parsable-style
```

### Large Markdown Files

**Problem:** Markdown file too large for viewer

**Solution:**
```bash
# Use compression or filtering
repomix --style markdown --compress --include "src/**"
```

### JSON Parsing Errors

**Problem:** Invalid JSON in output

**Solution:**
```bash
# Validate JSON
jq . output.json

# Check for syntax errors
repomix --style json --parsable-style
```

## Future Formats

Potential future formats:
- YAML (human-readable structured)
- HTML (web-ready documentation)
- PDF (distributable reports)
- SQLite (queryable database)
