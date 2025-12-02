# Repomix Workflows and Use Cases

Common workflows and patterns for using Repomix effectively in different scenarios.

## Workflow Approach Selection

Repomix provides three interaction levels. **Choose the right level for your task:**

### Decision Matrix

| Task Type | Recommended Approach | Alternative |
|-----------|---------------------|-------------|
| Explore unfamiliar codebase | `/repomix-explorer:explore-local` | MCP tools for specific patterns |
| Quick one-off packing | `/repomix-commands:pack-local` | MCP tools for exact control |
| Custom analysis workflow | MCP tools | CLI for automation |
| Repeated analysis | MCP tools + `.repomixrc.json` | CLI for CI/CD |
| CI/CD integration | CLI (`npx repomix`) | N/A |
| Security audit | MCP tools (uncompressed) | explore-local for initial scan |
| Documentation generation | MCP tools (compressed) | pack-local for quick export |
| Code review | `/repomix-explorer:explore-local` | MCP tools for detailed analysis |
| Learning patterns | `/repomix-explorer:explore-local` | N/A |
| Team standardization | MCP tools + config | Slash commands with shared config |

### Optimization Principle

**Start high-level (slash commands), drop down only when needed:**

```
/repomix-explorer:explore-local
    ↓ Need more control?
/repomix-commands:pack-local
    ↓ Need precise parameters?
MCP tools
    ↓ Need automation?
CLI
```

**See `claude_code_plugins.md` for complete decision tree and 10+ real-world scenarios.**

---

## Code Review Workflows

### Pre-Commit Review

Analyze uncommitted changes before committing.

**Approach 1: Slash Command (RECOMMENDED)**

```bash
# Natural language exploration
/repomix-explorer:explore-local What changes are uncommitted and are there any TODOs or debugging code?
```

**Advantages:**
- Fastest approach
- AI finds issues automatically
- Context-efficient

**Approach 2: MCP Tools (Custom Analysis)**

```typescript
// 1. Pack codebase
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'markdown',
  compress: false  // Keep implementation for review
});

// 2. Search for specific patterns
const todos = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'TODO|FIXME|HACK',
  ignoreCase: true,
  contextLines: 3
});

const debugging = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'console\\.log|debugger',
  contextLines: 2
});

// 3. Analyze findings
```

**Advantages:**
- Precise pattern control
- Multiple searches on same packed output
- Programmatic workflow

**Approach 3: CLI (Automation)**

```bash
#!/bin/bash
# Pre-commit hook integration
repomix --include-diffs --style markdown -o review.md

# Search for issues
grep -E "TODO|FIXME|console\.log" review.md
```

**Advantages:**
- Git hook integration
- No Claude Code dependency
- Automated enforcement

**Use When:**
- Before committing code
- Self-review workflow
- Checking for debugging code

### Pull Request Review

Comprehensive PR analysis with history:

```bash
# Pack with diffs and recent history
repomix --include-diffs --include-logs --include-logs-count 10 \
  --sort-by-changes --compress --style xml -o pr-review.xml
```

**MCP Workflow:**

```typescript
// 1. Pack with git context
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'xml',
  compress: true
});

// 2. Find authentication changes
const authCode = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'auth|login|session|token',
  ignoreCase: true,
  contextLines: 5
});

// 3. Find error handling
const errorHandling = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'try\\s*\\{|catch\\s*\\(',
  contextLines: 3
});

// 4. Provide comprehensive review
```

**Review Checklist:**
- Logic errors and bugs
- Security vulnerabilities
- Performance issues
- Code style consistency
- Test coverage
- Documentation
- Error handling

### Remote PR Review

Review PR without checking out locally:

```bash
# Pack PR branch from remote
repomix --remote user/repo/tree/pr-branch --compress --style json
```

**MCP Workflow:**

```typescript
// 1. Pack PR branch
const result = await mcp__repomix__pack_remote_repository({
  remote: 'https://github.com/user/repo/tree/feature-branch',
  style: 'json',
  compress: true
});

// 2. Analyze changes
// 3. Provide review comments
```

## Documentation Generation

### API Documentation

Extract and document public API surface:

```bash
# Pack source with compression (preserves signatures)
repomix --include "src/**" --compress --style markdown -o api-docs.md
```

**MCP Workflow:**

```typescript
// 1. Pack with compression
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'markdown',
  compress: true,
  includePatterns: 'src/**/*.ts,src/**/*.tsx'
});

// 2. Extract exported functions
const exports = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'export\\s+(function|class|interface|type|const)',
  contextLines: 10
});

// 3. Generate documentation from exports
```

**Generated Documentation Should Include:**
- Function signatures and parameters
- Return types
- JSDoc comments
- Usage examples
- Type definitions

### Architecture Documentation

Document overall project structure:

```bash
# Pack with full directory structure
repomix --include-full-directory-structure --compress \
  --style markdown -o architecture.md
```

**MCP Workflow:**

```typescript
// 1. Pack entire project
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'markdown',
  compress: true
});

// 2. Analyze structure from fileTree
// 3. Document:
//    - Directory organization
//    - Module boundaries
//    - Key components
//    - Data flow patterns
//    - Dependencies
```

### Changelog Generation

Generate changelog from git history:

```bash
# Pack with git logs
repomix --include-logs --include-logs-count 50 \
  --sort-by-changes --style markdown -o changelog-source.md
```

## Security Audit

### Vulnerability Scan

Find common security issues:

**MCP Workflow:**

```typescript
// 1. Pack codebase
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'xml'
});

// 2. SQL Injection patterns
const sqlInjection = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'execute.*\\+|query.*\\+|sql.*\\+',
  contextLines: 5
});

// 3. XSS vulnerabilities
const xss = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'innerHTML|dangerouslySetInnerHTML|document\\.write',
  contextLines: 3
});

// 4. Hardcoded credentials
const credentials = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'password\\s*=\\s*["\']|api_key\\s*=\\s*["\']',
  ignoreCase: true,
  contextLines: 2
});

// 5. Insecure random
const insecureRandom = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'Math\\.random',
  contextLines: 3
});

// 6. Report findings
```

**Security Patterns to Check:**
- SQL injection (string concatenation in queries)
- XSS (innerHTML, dangerouslySetInnerHTML)
- Command injection (exec, eval)
- Path traversal (file operations with user input)
- Hardcoded secrets
- Insecure randomness
- Weak cryptography
- CSRF vulnerabilities
- Authentication bypass

### Dependency Audit

Analyze third-party dependencies:

```bash
# Pack package files
repomix --include "**/package.json,**/requirements.txt,**/go.mod" \
  --style json -o dependencies.json
```

**MCP Workflow:**

```typescript
// 1. Pack dependency files
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  includePatterns: '**/package.json,**/yarn.lock,**/pnpm-lock.yaml'
});

// 2. Extract dependencies
const deps = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: '"[^"]+": "\\^?[0-9]',
  contextLines: 0
});

// 3. Analyze for:
//    - Outdated versions
//    - Known vulnerabilities
//    - License compliance
//    - Unused dependencies
```

## Refactoring

### Dead Code Detection

Find unused code:

**MCP Workflow:**

```typescript
// 1. Pack codebase
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: false  // Need implementation details
});

// 2. Find function definitions
const functions = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'function\\s+(\\w+)',
  contextLines: 0
});

// 3. For each function, search for usage
// 4. Identify functions with zero references
// 5. Generate dead code report
```

### Code Duplication

Find duplicated code patterns:

```bash
# Pack without compression to see implementation
repomix --style json -o duplication-analysis.json
```

**Analysis Steps:**
1. Pack codebase
2. Extract all functions
3. Compare function implementations
4. Identify similar/identical code blocks
5. Suggest refactoring opportunities

### Complexity Analysis

Identify complex functions for refactoring:

**MCP Workflow:**

```typescript
// 1. Pack codebase
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: false
});

// 2. Find large functions
const largeFunctions = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'function\\s+\\w+|const\\s+\\w+\\s*=\\s*\\(',
  afterLines: 100  // Get function body
});

// 3. Analyze each function:
//    - Line count
//    - Nesting depth
//    - Cyclomatic complexity
//    - Number of parameters
// 4. Flag functions exceeding thresholds
```

## Learning and Exploration

### Understand New Codebase

Explore unfamiliar projects:

```bash
# Pack with compression to see structure
repomix --compress --token-count-tree --style markdown -o overview.md
```

**Exploration Workflow:**

```typescript
// 1. Pack repository
const result = await mcp__repomix__pack_remote_repository({
  remote: 'facebook/react',
  compress: true,
  style: 'markdown'
});

// 2. Explore entry points
const entryPoints = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'export default|export \\*',
  contextLines: 5
});

// 3. Find core abstractions
const classes = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'class\\s+\\w+|interface\\s+\\w+',
  contextLines: 10
});

// 4. Understand data flow
const stateManagement = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'useState|useReducer|createContext',
  contextLines: 5
});

// 5. Summarize architecture and patterns
```

### Pattern Mining

Learn patterns from successful projects:

**MCP Workflow:**

```typescript
// 1. Pack reference project
const result = await mcp__repomix__pack_remote_repository({
  remote: 'vercel/next.js',
  compress: true,
  includePatterns: 'packages/**/*.ts'
});

// 2. Extract specific patterns (e.g., error handling)
const errorPatterns = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'try\\s*\\{[\\s\\S]*?catch\\s*\\(',
  contextLines: 10
});

// 3. Analyze and document patterns
```

### Technology Evaluation

Evaluate libraries before adoption:

```bash
# Pack library source
repomix --remote popular-library/repo --compress --style json
```

**Evaluation Criteria:**
- API surface and ergonomics
- Code quality and organization
- Test coverage
- Performance patterns
- Security practices
- Documentation quality

## Migration Planning

### Dependency Migration

Plan migration to new library version:

**MCP Workflow:**

```typescript
// 1. Pack current codebase
const current = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: false
});

// 2. Find all usages of old API
const oldAPI = await mcp__repomix__grep_repomix_output({
  outputId: current.outputId,
  pattern: 'oldLibrary\\.|import.*from.*oldLibrary',
  contextLines: 5
});

// 3. Pack new library examples
const newLib = await mcp__repomix__pack_remote_repository({
  remote: 'new-library/repo',
  compress: true,
  includePatterns: 'examples/**'
});

// 4. Create migration guide mapping old → new
```

### Framework Migration

Plan large-scale framework migrations:

```bash
# Pack current framework usage
repomix --include "**/*.jsx,**/*.js" --style xml -o react-usage.xml

# Pack target framework examples
repomix --remote vue/core --compress --style xml -o vue-patterns.xml
```

## Performance Optimization

### Bundle Size Analysis

Identify large dependencies:

```bash
# Pack with token count tree
repomix --token-count-tree 1000 --style json -o bundle-analysis.json
```

**MCP Workflow:**

```typescript
// 1. Pack with metrics
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  style: 'json',
  topFilesLength: 20
});

// 2. Analyze metrics.topFiles for largest files
// 3. Identify optimization opportunities:
//    - Large dependencies to replace
//    - Unused code to remove
//    - Code-splitting opportunities
```

### Performance Hotspot Detection

Find performance-critical code:

**MCP Workflow:**

```typescript
// 1. Pack codebase
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: false
});

// 2. Find loops in loops (O(n²) patterns)
const nestedLoops = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'for\\s*\\([\\s\\S]{0,100}for\\s*\\(',
  contextLines: 10
});

// 3. Find expensive operations
const expensiveOps = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'JSON\\.parse|JSON\\.stringify|sort\\(|filter\\(',
  contextLines: 5
});

// 4. Recommend optimizations
```

## Test Coverage Analysis

### Identify Untested Code

Find code without tests:

**MCP Workflow:**

```typescript
// 1. Pack source code
const source = await mcp__repomix__pack_codebase({
  directory: '/path/to/project/src',
  compress: true
});

// 2. Pack test code
const tests = await mcp__repomix__pack_codebase({
  directory: '/path/to/project/test',
  compress: false
});

// 3. Extract source functions
const sourceFunctions = await mcp__repomix__grep_repomix_output({
  outputId: source.outputId,
  pattern: 'export\\s+(function|class)\\s+(\\w+)',
  contextLines: 0
});

// 4. Search for each function in tests
// 5. Report untested functions
```

## Compliance and Standards

### License Compliance

Check license usage across dependencies:

```bash
# Pack license files
repomix --include "**/LICENSE,**/package.json" --style json
```

### Coding Standards Audit

Verify adherence to coding standards:

**MCP Workflow:**

```typescript
// 1. Pack codebase
const result = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: false
});

// 2. Check for var usage (should use const/let)
const varUsage = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: '\\bvar\\s+',
  contextLines: 2
});

// 3. Check for console.log (should be removed)
const consoleLogs = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'console\\.(log|debug|info)',
  contextLines: 2
});

// 4. Check for proper error handling
const errorHandling = await mcp__repomix__grep_repomix_output({
  outputId: result.outputId,
  pattern: 'catch\\s*\\(\\w+\\)\\s*\\{\\s*\\}',
  contextLines: 3
});

// 5. Generate compliance report
```

## Best Practices by Use Case

### Quick Analysis
- Use compression
- Use XML or JSON format
- Filter with includePatterns

### Deep Dive
- Don't compress
- Use Markdown format
- Include git history
- Show line numbers

### Large Codebases
- Always compress
- Use token count tree
- Aggressive filtering
- Iterative analysis with grep

### Security Focused
- Don't compress
- Enable security scanning
- Include all file types
- Check for patterns

### Documentation
- Compress
- Filter to source files only
- Include README and docs
- Markdown format

### Learning
- Compress
- Include examples directory
- Focus on public API
- Markdown format
