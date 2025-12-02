# Repomix MCP Tools Reference

Complete reference for Repomix MCP server tools available in Claude Code.

## Overview

**MCP tools are low-level primitives** for programmatic codebase packing and analysis.

### When to Use MCP Tools

MCP tools provide precise control and are ideal for:
- ✅ Custom workflows requiring exact parameters
- ✅ Repeated analysis with consistent settings
- ✅ Multi-stage analysis (pack once, grep multiple times)
- ✅ Team standardization via `.repomixrc.json`
- ✅ Combining multiple operations programmatically

### Consider Higher-Level Alternatives First

For most interactive tasks, **start with Claude Code slash commands:**

- **`/repomix-explorer:explore-local`** - AI-driven codebase exploration
  - Most context-efficient approach
  - Natural language queries: "Find authentication logic"
  - Automated pack → search → analyze workflow
  - Use for: 90% of exploratory analysis tasks

- **`/repomix-commands:pack-local`** - Natural language packing
  - Simple directives: "Pack as markdown with compression"
  - No parameter syntax required
  - Use for: Quick one-off packing

**Optimization Hierarchy:**
```
1. Slash Commands (RECOMMENDED) - Natural language, AI-driven
2. MCP Tools (THIS DOCUMENT) - Precise control, custom workflows
3. CLI (npx repomix) - Automation, CI/CD only
```

**See `claude_code_plugins.md` for complete optimization guide and decision tree.**

---

## MCP Server Configuration

Add to `.mcp.json`:

```json
{
  "mcpServers": {
    "repomix": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "repomix", "--mcp"],
      "env": {}
    }
  }
}
```

Add to `.claude/settings.json` permissions:

```json
{
  "permissions": {
    "allow": [
      "mcp__repomix"
    ]
  }
}
```

## Available MCP Tools

### pack_codebase

Pack a local directory into an AI-optimized single file.

**Purpose:** Consolidate local codebase for analysis, code review, documentation generation, or bug investigation.

**Parameters:**

```typescript
{
  directory: string;              // Absolute path to directory
  style?: 'xml' | 'markdown' | 'json' | 'plain';  // Output format (default: 'xml')
  compress?: boolean;             // Enable Tree-sitter compression (default: false)
  includePatterns?: string;       // Glob patterns, comma-separated
  ignorePatterns?: string;        // Glob patterns, comma-separated
  topFilesLength?: number;        // Top N files in metrics (default: 10)
}
```

**Example Usage:**

```typescript
// Pack current project
await mcp__repomix__pack_codebase({
  directory: '/Users/user/project',
  style: 'xml',
  compress: false,
  topFilesLength: 10
});

// Pack with filtering
await mcp__repomix__pack_codebase({
  directory: '/Users/user/project',
  style: 'json',
  includePatterns: 'src/**/*.ts,**/*.md',
  ignorePatterns: '**/*.test.ts,**/*.spec.ts',
  compress: true
});

// Pack specific subdirectory
await mcp__repomix__pack_codebase({
  directory: '/Users/user/project/src',
  style: 'markdown',
  compress: true
});
```

**Returns:**

```typescript
{
  outputId: string;           // ID for subsequent operations
  metrics: {
    totalFiles: number;
    totalLines: number;
    totalTokens: number;
    topFiles: Array<{
      fileName: string;
      lines: number;
      tokens: number;
    }>;
  };
  fileTree: string;           // Directory structure
  codeContent: string;        // Formatted code (first 30k chars)
}
```

**Use Cases:**
- Code review preparation
- Documentation generation
- Bug investigation
- Architecture analysis
- Refactoring planning

**Compression Benefits:**
- ~70% token reduction
- Preserves: imports, exports, signatures, types, interfaces, comments
- Removes: function bodies, implementation details

### pack_remote_repository

Clone and pack a GitHub repository.

**Purpose:** Analyze remote repositories without local cloning, perfect for exploring open-source projects or reviewing PRs.

**Parameters:**

```typescript
{
  remote: string;                 // GitHub URL or user/repo
  style?: 'xml' | 'markdown' | 'json' | 'plain';  // Output format (default: 'xml')
  compress?: boolean;             // Enable Tree-sitter compression (default: false)
  includePatterns?: string;       // Glob patterns, comma-separated
  ignorePatterns?: string;        // Glob patterns, comma-separated
  topFilesLength?: number;        // Top N files in metrics (default: 10)
}
```

**Example Usage:**

```typescript
// Pack GitHub repository (short form)
await mcp__repomix__pack_remote_repository({
  remote: 'yamadashy/repomix',
  style: 'xml',
  compress: true
});

// Pack with full URL
await mcp__repomix__pack_remote_repository({
  remote: 'https://github.com/anthropics/anthropic-sdk-typescript',
  style: 'json',
  compress: true,
  topFilesLength: 15
});

// Pack specific branch
await mcp__repomix__pack_remote_repository({
  remote: 'https://github.com/user/repo/tree/develop',
  style: 'markdown',
  compress: true
});

// Pack with file filtering
await mcp__repomix__pack_remote_repository({
  remote: 'vercel/next.js',
  includePatterns: 'packages/**/*.ts,**/*.md',
  ignorePatterns: '**/*.test.ts,examples/**',
  compress: true
});
```

**Returns:**

Same as `pack_codebase`:

```typescript
{
  outputId: string;
  metrics: { ... };
  fileTree: string;
  codeContent: string;
}
```

**Use Cases:**
- Open-source project exploration
- Competitor analysis
- Library evaluation
- PR review preparation
- Learning from examples

**URL Formats Supported:**
- `user/repo`
- `https://github.com/user/repo`
- `https://github.com/user/repo/tree/branch`
- `https://github.com/user/repo/tree/tag`

### attach_packed_output

Attach existing Repomix output file for analysis.

**Purpose:** Work with previously generated packed outputs without re-processing, useful for iterative analysis or sharing packed codebases.

**Parameters:**

```typescript
{
  path: string;                   // Path to directory or file
  topFilesLength?: number;        // Top N files in metrics (default: 10)
}
```

**Example Usage:**

```typescript
// Attach from directory containing repomix-output.xml
await mcp__repomix__attach_packed_output({
  path: '/Users/user/project',
  topFilesLength: 10
});

// Attach specific file
await mcp__repomix__attach_packed_output({
  path: '/Users/user/project/repomix-output.xml'
});

// Attach different format
await mcp__repomix__attach_packed_output({
  path: '/Users/user/packed-repo.json'
});
```

**Returns:**

Same as `pack_codebase`:

```typescript
{
  outputId: string;
  metrics: { ... };
  fileTree: string;
  codeContent: string;
}
```

**Use Cases:**
- Iterative analysis on same codebase
- Sharing packed outputs across team
- Re-analyzing with different approaches
- Working offline with pre-packed repositories

**Supported Formats:**
- `.xml` (repomix-output.xml)
- `.md` (repomix-output.md)
- `.json` (repomix-output.json)
- `.txt` (repomix-output.txt)

**Refresh Behavior:**
Calling `attach_packed_output` again with same path refreshes content if file updated. Returns new outputId.

### read_repomix_output

Read contents of packed output file with optional line range.

**Purpose:** Retrieve specific sections of large packed outputs, useful when full output exceeds context limits.

**Parameters:**

```typescript
{
  outputId: string;               // ID from pack operation
  startLine?: number;             // Starting line (1-based, inclusive)
  endLine?: number;               // Ending line (1-based, inclusive)
}
```

**Example Usage:**

```typescript
// Read entire output
await mcp__repomix__read_repomix_output({
  outputId: '8f7d3b1e2a9c6054'
});

// Read specific range
await mcp__repomix__read_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  startLine: 100,
  endLine: 200
});

// Read from start
await mcp__repomix__read_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  startLine: 1,
  endLine: 1000
});

// Read to end
await mcp__repomix__read_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  startLine: 5000
});
```

**Returns:**

```typescript
{
  content: string;                // File content for range
  totalLines: number;             // Total lines in file
  startLine: number;              // Actual start line
  endLine: number;                // Actual end line
}
```

**Use Cases:**
- Working with large packed outputs
- Incremental analysis
- Targeted file inspection
- Memory-constrained environments

**Line Numbering:**
- 1-based indexing (first line is 1)
- Inclusive ranges (both start and end included)
- Omitted startLine: reads from beginning
- Omitted endLine: reads to end

### grep_repomix_output

Search packed output using JavaScript RegExp patterns.

**Purpose:** Find specific patterns, functions, classes, or code across packed codebase without loading entire output into context.

**Parameters:**

```typescript
{
  outputId: string;               // ID from pack operation
  pattern: string;                // JavaScript RegExp pattern
  ignoreCase?: boolean;           // Case-insensitive (default: false)
  contextLines?: number;          // Lines before and after (default: 0)
  beforeLines?: number;           // Lines before match (overrides contextLines)
  afterLines?: number;            // Lines after match (overrides contextLines)
}
```

**Example Usage:**

```typescript
// Find function definitions
await mcp__repomix__grep_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  pattern: 'function\\s+\\w+\\s*\\(',
  contextLines: 3
});

// Find TODOs and FIXMEs (case-insensitive)
await mcp__repomix__grep_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  pattern: 'TODO|FIXME',
  ignoreCase: true,
  beforeLines: 2,
  afterLines: 5
});

// Find class definitions
await mcp__repomix__grep_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  pattern: 'class\\s+\\w+',
  contextLines: 5
});

// Find specific API calls
await mcp__repomix__grep_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  pattern: 'fetch\\(.*\\)',
  contextLines: 3
});

// Find imports
await mcp__repomix__grep_repomix_output({
  outputId: '8f7d3b1e2a9c6054',
  pattern: 'import.*from.*react',
  ignoreCase: true
});
```

**Returns:**

```typescript
{
  matches: Array<{
    file: string;                 // File path
    lineNumber: number;           // Line number of match
    line: string;                 // Matching line
    context: {
      before: string[];           // Lines before match
      after: string[];            // Lines after match
    };
  }>;
  totalMatches: number;           // Total number of matches
}
```

**Use Cases:**
- Find authentication code
- Locate error handling patterns
- Discover API usage
- Find security vulnerabilities
- Identify code smells
- Locate dependencies

**Pattern Syntax:**
JavaScript RegExp syntax (NOT grep/ripgrep syntax)

**Common Patterns:**

```typescript
// Function definitions
'function\\s+\\w+\\s*\\('
'const\\s+\\w+\\s*=\\s*\\('
'async\\s+function\\s+\\w+'

// Class definitions
'class\\s+\\w+\\s*(extends|\\{)'
'interface\\s+\\w+'

// Imports/Exports
'import.*from.*["\'].*["\']'
'export\\s+(default|const|function|class)'

// API calls
'fetch\\('
'axios\\.'
'http\\.get\\('

// Error handling
'try\\s*\\{'
'catch\\s*\\('
'throw\\s+new\\s+'

// Comments
'\\/\\/.*TODO'
'\\/\\*.*FIXME'

// Environment variables
'process\\.env\\.'
'process\\.env\\[.*\\]'
```

## file_system_read_file

Read file from local filesystem with security validation.

**Purpose:** Direct file access for MCP operations, with built-in sensitive data detection.

**Parameters:**

```typescript
{
  path: string;                   // Absolute file path
}
```

**Security Features:**
- Detects API keys
- Detects passwords
- Detects secrets
- Prevents accidental sensitive data exposure

**Example Usage:**

```typescript
await mcp__repomix__file_system_read_file({
  path: '/Users/user/project/src/config.ts'
});
```

## file_system_read_directory

List directory contents with formatted output.

**Purpose:** Explore project structure and understand codebase organization.

**Parameters:**

```typescript
{
  path: string;                   // Absolute directory path
}
```

**Returns:**

```typescript
{
  contents: string;               // Formatted list with [FILE]/[DIR] indicators
}
```

**Example Usage:**

```typescript
await mcp__repomix__file_system_read_directory({
  path: '/Users/user/project/src'
});
```

**Output Format:**

```
[DIR] components
[DIR] utils
[FILE] index.ts
[FILE] app.ts
[FILE] config.json
```

## Workflow Examples

### Code Review Workflow

```typescript
// 1. Pack repository with diffs and logs
const result = await mcp__repomix__pack_codebase({
  directory: '/Users/user/project',
  style: 'markdown',
  compress: false
});

// 2. Search for changed authentication code
const authChanges = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'auth.*|login.*|token.*',
  ignoreCase: true,
  contextLines: 5
});

// 3. Analyze results and provide review
```

### Security Audit Workflow

```typescript
// 1. Pack with security scanning
const result = await mcp__repomix__pack_codebase({
  directory: '/Users/user/project',
  style: 'xml'
});

// 2. Search for potential vulnerabilities
const sqlInjection = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'execute.*\\+.*|query.*\\+.*',
  contextLines: 5
});

const xss = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'innerHTML.*=|dangerouslySetInnerHTML',
  contextLines: 3
});

// 3. Report findings
```

### Documentation Generation Workflow

```typescript
// 1. Pack with compression
const result = await mcp__repomix__pack_codebase({
  directory: '/Users/user/project',
  style: 'markdown',
  compress: true,
  includePatterns: 'src/**,**/*.md'
});

// 2. Extract public API
const publicAPI = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'export\\s+(function|class|interface|type)',
  contextLines: 10
});

// 3. Generate documentation from API surface
```

### Remote Repository Analysis

```typescript
// 1. Pack remote repository
const result = await mcp__repomix__pack_remote_repository({
  remote: 'facebook/react',
  style: 'json',
  compress: true,
  includePatterns: 'packages/**/*.ts'
});

// 2. Search for hooks implementation
const hooks = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'function\\s+use\\w+',
  contextLines: 5
});

// 3. Analyze patterns
```

### Iterative Analysis

```typescript
// 1. Pack once
const result = await mcp__repomix__pack_codebase({
  directory: '/Users/user/project',
  compress: true
});

// 2. Multiple searches on same output
const functions = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'function'
});

const classes = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'class'
});

const imports = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'import'
});

// 3. Efficient: no repacking needed
```

## Best Practices

1. **Use compression for large codebases** to reduce token usage
2. **Filter with includePatterns** to focus analysis on relevant files
3. **Use grep incrementally** instead of reading entire output
4. **Attach pre-packed outputs** for repeated analysis
5. **Combine tools** for powerful workflows (pack → grep → analyze)
6. **Leverage context parameters** in grep to understand surrounding code
7. **Use appropriate output format** for use case (XML for structure, Markdown for readability, JSON for parsing)
8. **Enable security scanning** to prevent sensitive data exposure
9. **Pack remote repos** to analyze without cloning
10. **Use read_repomix_output with ranges** for very large outputs
