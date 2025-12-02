# Repomix Configuration Guide

Complete reference for `repomix.config.json`, `repomix.config.js`, and `repomix.config.ts` configuration files.

## Configuration File Priority

Repomix checks for configuration files in this order:
1. `repomix.config.ts` (TypeScript) - **RECOMMENDED**
2. `repomix.config.js` (JavaScript)
3. `repomix.config.json` (JSON/JSON5)

TypeScript configuration provides:
- Type checking and IDE autocomplete
- Better developer experience
- Compile-time validation

## Complete Configuration Schema

```typescript
// repomix.config.ts
import { defineConfig } from 'repomix';

export default defineConfig({
  // Input settings
  input: {
    maxFileSize: 50000000  // 50MB, files larger than this are skipped
  },

  // Output settings
  output: {
    // File path
    filePath: 'repomix-output.xml',

    // Output format: 'xml' | 'markdown' | 'json' | 'plain'
    style: 'xml',

    // Escape special characters for valid formatting
    parsableStyle: false,

    // Enable Tree-sitter compression (~70% token reduction)
    compress: false,

    // Custom header text in output
    headerText: 'Custom header information for the packed file.',

    // Include sections
    fileSummary: true,              // Metrics summary
    directoryStructure: true,       // File tree
    files: true,                    // File contents

    // Comment and line handling
    removeComments: false,
    removeEmptyLines: false,

    // File summary settings
    topFilesLength: 5,              // Number of largest files to show

    // Token count visualization
    tokenCountTree: false,          // Show hierarchical token counts

    // Line numbers and formatting
    showLineNumbers: false,
    truncateBase64: false,          // Truncate base64 encoded content

    // Output destination
    copyToClipboard: false,

    // Directory handling
    includeEmptyDirectories: false,

    // Git integration
    git: {
      sortByChanges: true,          // Sort files by recent changes
      sortByChangesMaxCommits: 100, // Commits to analyze for sorting
      includeDiffs: false,          // Include uncommitted changes
      includeLogs: false,           // Include commit history
      includeLogsCount: 50          // Number of log entries
    }
  },

  // File inclusion patterns (glob syntax)
  include: ['**/*'],

  // Ignore patterns
  ignore: {
    useGitignore: true,             // Respect .gitignore
    useDefaultPatterns: true,       // Use built-in ignore patterns
    customPatterns: [               // Additional patterns
      'additional-folder',
      '**/*.log'
    ]
  },

  // Security settings
  security: {
    enableSecurityCheck: true       // Use Secretlint scanning
  },

  // Token counting
  tokenCount: {
    encoding: 'o200k_base'          // Encoding for token calculation
  }
});
```

## Configuration Sections

### Input Configuration

```json
{
  "input": {
    "maxFileSize": 50000000
  }
}
```

**maxFileSize**: Maximum file size in bytes. Files exceeding this limit are skipped.

Default: 50MB (50,000,000 bytes)

Use cases:
- Prevent memory issues with very large files
- Skip generated files that are too large
- Control output size

### Output Configuration

#### Basic Output Settings

```json
{
  "output": {
    "filePath": "repomix-output.xml",
    "style": "xml"
  }
}
```

**filePath**: Output file name and path

**style**: Output format
- `xml`: Structured XML format (default)
- `markdown`: Human-readable markdown
- `json`: Machine-readable JSON
- `plain`: Simple plain text

#### Format Options

```json
{
  "output": {
    "parsableStyle": false,
    "headerText": "Custom header",
    "fileSummary": true,
    "directoryStructure": true,
    "files": true
  }
}
```

**parsableStyle**: Escape special characters for valid formatting

**headerText**: Custom text at the beginning of output

**fileSummary**: Include metrics and statistics

**directoryStructure**: Include file tree visualization

**files**: Include actual file contents

#### Code Processing

```json
{
  "output": {
    "compress": false,
    "removeComments": false,
    "removeEmptyLines": false,
    "truncateBase64": false
  }
}
```

**compress**: Enable Tree-sitter code compression
- Reduces token count by ~70%
- Preserves signatures, types, interfaces
- Removes implementation details

**removeComments**: Strip all comments from code
- Reduces output size
- Loses documentation context

**removeEmptyLines**: Remove blank lines
- Further size reduction
- May reduce readability

**truncateBase64**: Truncate base64 encoded content
- Prevents large data URIs from bloating output

#### Display Options

```json
{
  "output": {
    "topFilesLength": 5,
    "tokenCountTree": false,
    "showLineNumbers": false,
    "includeEmptyDirectories": false
  }
}
```

**topFilesLength**: Number of largest files shown in summary

**tokenCountTree**: Display hierarchical token count visualization

**showLineNumbers**: Add line numbers to code in output

**includeEmptyDirectories**: Include empty directories in tree

#### Output Destination

```json
{
  "output": {
    "copyToClipboard": false
  }
}
```

**copyToClipboard**: Copy output to system clipboard after generation

#### Git Integration

```json
{
  "output": {
    "git": {
      "sortByChanges": true,
      "sortByChangesMaxCommits": 100,
      "includeDiffs": false,
      "includeLogs": false,
      "includeLogsCount": 50
    }
  }
}
```

**sortByChanges**: Sort files by recent modification frequency
- Prioritizes frequently changed files
- Useful for identifying active development areas

**sortByChangesMaxCommits**: Number of recent commits to analyze

**includeDiffs**: Include uncommitted changes (git diff)
- Shows work in progress
- Useful for code review

**includeLogs**: Include commit history (git log)

**includeLogsCount**: Number of log entries to include

### Include Patterns

```json
{
  "include": [
    "**/*",
    "src/**/*.ts",
    "**/*.{js,ts,tsx}"
  ]
}
```

Glob patterns for files to include:
- `**/*`: All files (default)
- `src/**/*.ts`: All TypeScript files in src
- `**/*.{js,ts,tsx}`: Multiple extensions

Patterns use [fast-glob](https://github.com/mrmlnc/fast-glob) syntax.

### Ignore Configuration

```json
{
  "ignore": {
    "useGitignore": true,
    "useDefaultPatterns": true,
    "customPatterns": [
      "additional-folder",
      "**/*.log",
      "temp/**"
    ]
  }
}
```

**Ignore Priority (highest to lowest):**
1. Custom patterns
2. `.repomixignore` file
3. `.gitignore` (if useGitignore: true)
4. Default patterns (if useDefaultPatterns: true)

**useGitignore**: Respect .gitignore rules

**useDefaultPatterns**: Use built-in ignore patterns

Built-in patterns include:
- `node_modules/**`
- `.git/**`
- `dist/**`, `build/**`
- `**/*.min.js`, `**/*.bundle.js`
- Binary files and archives

**customPatterns**: Additional patterns to ignore

### .repomixignore File

Alternative to config file ignore patterns:

```
# .repomixignore
# Ignore patterns (one per line)
*.log
temp/
**/*.test.ts
```

Syntax identical to .gitignore

### Security Configuration

```json
{
  "security": {
    "enableSecurityCheck": true
  }
}
```

**enableSecurityCheck**: Enable Secretlint security scanning

Detects:
- API keys
- Tokens and secrets
- Credentials
- Private keys
- Sensitive patterns

When detected: Process halts with error, preventing sensitive data exposure

### Token Count Configuration

```json
{
  "tokenCount": {
    "encoding": "o200k_base"
  }
}
```

**encoding**: Tokenizer encoding for counting

Available encodings:
- `o200k_base`: GPT-4o, GPT-4o mini (RECOMMENDED)
- `cl100k_base`: GPT-4, GPT-3.5-turbo
- `p50k_base`: Codex models
- `r50k_base`: GPT-3 models

## Configuration Examples

### Minimal Configuration

```json
{
  "$schema": "https://repomix.com/schemas/latest/schema.json",
  "output": {
    "style": "xml"
  }
}
```

### Compressed Analysis

```typescript
import { defineConfig } from 'repomix';

export default defineConfig({
  output: {
    style: 'json',
    compress: true,
    removeComments: true,
    topFilesLength: 10
  }
});
```

### Code Review Focused

```json
{
  "$schema": "https://repomix.com/schemas/latest/schema.json",
  "output": {
    "style": "markdown",
    "compress": false,
    "removeComments": false,
    "showLineNumbers": true,
    "git": {
      "sortByChanges": true,
      "sortByChangesMaxCommits": 50,
      "includeDiffs": true,
      "includeLogs": true,
      "includeLogsCount": 20
    }
  }
}
```

### TypeScript Project

```typescript
import { defineConfig } from 'repomix';

export default defineConfig({
  include: [
    'src/**/*.ts',
    'src/**/*.tsx',
    '**/*.json'
  ],
  output: {
    style: 'xml',
    compress: true,
    filePath: 'typescript-analysis.xml'
  },
  ignore: {
    useGitignore: true,
    customPatterns: [
      '**/*.test.ts',
      '**/*.spec.ts',
      'coverage/**'
    ]
  }
});
```

### Security-Focused Analysis

```json
{
  "$schema": "https://repomix.com/schemas/latest/schema.json",
  "include": [
    "**/*.{js,ts,py,go,java}"
  ],
  "output": {
    "style": "xml",
    "removeComments": false,
    "filePath": "security-audit.xml"
  },
  "security": {
    "enableSecurityCheck": true
  },
  "ignore": {
    "customPatterns": [
      "**/test/**",
      "**/*.min.js"
    ]
  }
}
```

### Documentation Generation

```typescript
import { defineConfig } from 'repomix';

export default defineConfig({
  include: [
    'src/**',
    '**/*.md'
  ],
  output: {
    style: 'markdown',
    compress: true,
    removeComments: true,
    removeEmptyLines: true,
    filePath: 'documentation.md',
    headerText: 'Project Documentation'
  },
  ignore: {
    customPatterns: [
      '**/node_modules/**',
      '**/*.test.*'
    ]
  }
});
```

### Large Codebase Optimization

```json
{
  "$schema": "https://repomix.com/schemas/latest/schema.json",
  "input": {
    "maxFileSize": 10000000
  },
  "output": {
    "style": "json",
    "compress": true,
    "removeComments": true,
    "removeEmptyLines": true,
    "tokenCountTree": true,
    "filePath": "compressed-analysis.json"
  },
  "ignore": {
    "customPatterns": [
      "**/*.min.*",
      "**/dist/**",
      "**/build/**"
    ]
  }
}
```

## Schema Validation

TypeScript configuration provides automatic validation:

```typescript
import { defineConfig } from 'repomix';
import type { RepomixConfigMerged } from 'repomix';

export default defineConfig({
  // TypeScript will validate all options
  // IDE provides autocomplete
  output: {
    style: 'invalid'  // TypeScript error!
  }
});
```

JSON schema validation:

```json
{
  "$schema": "https://repomix.com/schemas/latest/schema.json"
}
```

## Environment-Specific Configurations

```typescript
// repomix.config.ts
import { defineConfig } from 'repomix';

const isDevelopment = process.env.NODE_ENV === 'development';

export default defineConfig({
  output: {
    style: isDevelopment ? 'markdown' : 'json',
    compress: !isDevelopment,
    verbose: isDevelopment
  }
});
```

## Best Practices

1. **Use TypeScript configuration** for type safety and autocomplete
2. **Include schema reference** in JSON files for validation
3. **Start minimal** and add options as needed
4. **Use compression** for large codebases to reduce token usage
5. **Enable security checks** to prevent sensitive data leaks
6. **Document custom patterns** with comments explaining why files are ignored
7. **Test configuration** with `--verbose` flag to verify settings
8. **Version control** configuration files for team consistency
9. **Create multiple configs** for different use cases (review, docs, audit)
10. **Use `.repomixignore`** for large ignore lists to keep config clean
