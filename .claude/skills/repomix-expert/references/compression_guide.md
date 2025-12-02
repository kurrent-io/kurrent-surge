# Tree-sitter Code Compression Guide

Complete guide to Repomix's Tree-sitter compression feature for reducing token usage while preserving code meaning.

## What is Tree-sitter Compression?

Tree-sitter compression uses Tree-sitter parsers to intelligently extract code structure while removing implementation details. This reduces token count by approximately 70% while maintaining semantic understanding.

## How It Works

### Parser-Based Extraction

Tree-sitter compression:
1. Parses code into Abstract Syntax Tree (AST)
2. Extracts structurally significant nodes
3. Removes implementation details
4. Preserves signatures, types, and interfaces
5. Maintains comments and documentation

### What Gets Preserved

**Always Preserved:**
- Import statements
- Export statements
- Function signatures
- Class definitions
- Interface definitions
- Type definitions
- Method signatures
- Comments and JSDoc
- Variable declarations
- Enum definitions

**Example - TypeScript:**

Original (100 tokens):
```typescript
import { ShoppingItem } from './shopping-item';

/**
 * Calculate the total price of shopping items
 */
const calculateTotal = (
  items: ShoppingItem[]
): number => {
  let total = 0;
  for (const item of items) {
    total += item.price * item.quantity;
  }
  return total;
}

interface Item {
  name: string;
  price: number;
  quantity: number;
}
```

Compressed (30 tokens):
```typescript
import { ShoppingItem } from './shopping-item';
⋮----
/**
 * Calculate the total price of shopping items
 */
const calculateTotal = (
  items: ShoppingItem[]
): number => {
⋮----
interface Item {
  name: string;
  price: number;
  quantity: number;
}
```

**Reduction:** 70% fewer tokens

### What Gets Removed

**Removed Implementation Details:**
- Function bodies
- Method implementations
- Loop contents
- Conditional logic
- Expression details
- Variable assignments (except declarations)

**Removal Principle:**
The implementation details are replaced with `⋮----` ellipsis markers, indicating omitted code.

## Supported Languages

Tree-sitter compression works with languages that have Tree-sitter parsers:

**Fully Supported:**
- JavaScript (.js, .mjs, .cjs)
- TypeScript (.ts, .tsx)
- Python (.py)
- Rust (.rs)
- Go (.go)
- Java (.java)
- C (.c, .h)
- C++ (.cpp, .hpp, .cc)
- C# (.cs)
- Ruby (.rb)
- PHP (.php)
- Swift (.swift)
- Kotlin (.kt)

**Partial Support:**
- Other languages may have limited support
- Falls back to original content if parser unavailable

## When to Use Compression

### Use Compression When:

1. **Analyzing large codebases**
   - Reduces context usage
   - Fits more code into LLM context window
   - Enables analysis of repositories that would exceed limits

2. **Understanding architecture**
   - Structure is more important than implementation
   - Focus on interfaces and contracts
   - High-level overview needed

3. **API documentation**
   - Public API surface is the focus
   - Implementation details irrelevant
   - Type signatures and parameters are key

4. **Code review (first pass)**
   - Quick overview of changes
   - Structural changes more important than implementation
   - Identify areas needing detailed review

5. **Dependency analysis**
   - Understanding module exports
   - API contracts between modules
   - Import/export relationships

6. **Type checking**
   - Type definitions and interfaces
   - Function signatures
   - Type constraints

7. **Refactoring planning**
   - Understanding current structure
   - Identifying module boundaries
   - Planning architectural changes

### Don't Use Compression When:

1. **Bug investigation**
   - Need implementation details
   - Logic errors require full code
   - Step-by-step execution understanding needed

2. **Performance optimization**
   - Algorithm implementation matters
   - Loop structure is critical
   - Implementation efficiency is focus

3. **Security audit**
   - Implementation vulnerabilities
   - Logic errors in validation
   - Actual code execution paths matter

4. **Test writing**
   - Need to understand behavior
   - Edge cases require implementation details
   - Full context needed for test coverage

5. **Detailed code review**
   - Logic correctness is critical
   - Implementation quality matters
   - Need to verify actual behavior

6. **Migration/refactoring execution**
   - Actual code needs to be modified
   - Implementation patterns must be updated
   - Can't work from signatures alone

## Compression Strategies

### Strategy 1: Progressive Compression

Start compressed, expand as needed:

```typescript
// 1. Initial compressed overview
const overview = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: true
});

// 2. Identify areas of interest from structure

// 3. Re-pack specific areas without compression
const detailed = await mcp__repomix__pack_codebase({
  directory: '/path/to/project/src/critical',
  compress: false
});

// 4. Analyze with full implementation details
```

### Strategy 2: Hybrid Approach

Compress some parts, keep others full:

```bash
# Compress utility code, keep core logic full
repomix --include "src/core/**" --compress=false -o core.xml
repomix --include "src/utils/**" --compress=true -o utils.xml
```

### Strategy 3: Compression for Filtering

Use compression to identify interesting files:

```typescript
// 1. Compressed overview with token counts
const overview = await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: true,
  style: 'json',
  topFilesLength: 20
});

// 2. Analyze metrics.topFiles
// 3. Re-pack largest/most complex files without compression
```

## Performance Impact

### Token Reduction

**Average Reductions:**
- TypeScript/JavaScript: 70-75%
- Python: 65-70%
- Go: 60-70%
- Java: 70-75%
- C/C++: 65-70%

**Factors Affecting Reduction:**
- Code density (more implementation = more reduction)
- Comment density (comments preserved)
- Function size (larger functions compress more)
- Type complexity (complex types preserved)

### Processing Time

**Compression Overhead:**
- Parsing: 2-3x slower than plain copy
- Still very fast: ~500-1000 files/second
- Worthwhile for large codebases

**Example:**
```
1000 files, 10MB source
- No compression: 0.5 seconds
- With compression: 1.5 seconds
- Output size: 3MB (70% reduction)
```

## Compression Configuration

### CLI

```bash
# Enable compression
repomix --compress

# Compression with remote repository
repomix --remote user/repo --compress

# Compression with filtering
repomix --compress --include "src/**/*.ts"
```

### Configuration File

```typescript
// repomix.config.ts
import { defineConfig } from 'repomix';

export default defineConfig({
  output: {
    compress: true
  }
});
```

```json
// repomix.config.json
{
  "output": {
    "compress": true
  }
}
```

### MCP Tools

```typescript
// Pack with compression
await mcp__repomix__pack_codebase({
  directory: '/path/to/project',
  compress: true
});

await mcp__repomix__pack_remote_repository({
  remote: 'user/repo',
  compress: true
});
```

## Compression Quality

### Verification

Check compression quality:

```bash
# Generate both versions
repomix -o full.xml
repomix --compress -o compressed.xml

# Compare sizes
ls -lh full.xml compressed.xml

# Compare token counts (shown in output)
```

### Adjusting Compression

If compression is too aggressive:
1. Don't compress critical modules
2. Use hybrid approach
3. Re-pack specific files without compression

If compression isn't enough:
1. Add `removeComments` option
2. Add `removeEmptyLines` option
3. Filter more aggressively with `include` patterns

## Language-Specific Behavior

### TypeScript/JavaScript

Preserves:
- Import/export statements
- Type annotations
- Interface/type definitions
- Function signatures
- Class structure
- JSDoc comments

Removes:
- Function bodies
- Arrow function implementations
- Object literal contents (except types)
- Array/loop contents

### Python

Preserves:
- Import statements
- Function definitions (def)
- Class definitions
- Decorators
- Docstrings
- Type hints

Removes:
- Function bodies
- Class method implementations
- Logic and expressions

### Go

Preserves:
- Package declarations
- Import statements
- Function signatures
- Interface definitions
- Struct definitions
- Comments

Removes:
- Function bodies
- Method implementations
- Logic

### Java

Preserves:
- Package declarations
- Import statements
- Class declarations
- Interface definitions
- Method signatures
- Annotations
- Javadoc comments

Removes:
- Method bodies
- Constructor implementations
- Logic

## Advanced Patterns

### Selective Compression

Compress based on file patterns:

```bash
# Compress tests, keep source full
repomix --include "src/**" -o src-full.xml
repomix --include "test/**" --compress -o test-compressed.xml
```

### Compression for Large Monorepos

```bash
# Compress all, then drill down
repomix --compress -o overview.xml
repomix --include "packages/critical/**" -o critical-detail.xml
```

### Compression with Git Integration

```bash
# Compressed structure + recent changes
repomix --compress --include-diffs --include-logs-count 10
```

## Troubleshooting

### Compression Too Aggressive

**Problem:** Lost important context

**Solution:**
```bash
# Don't compress
repomix --compress=false

# Or compress less aggressively by preserving comments
repomix --compress --remove-comments=false
```

### Parser Errors

**Problem:** Tree-sitter parser fails on certain files

**Solution:**
- File falls back to full content automatically
- Check for syntax errors in source file
- Update Tree-sitter parsers

### Unexpected Output

**Problem:** Compression not working as expected

**Solution:**
```bash
# Verify with verbose logging
repomix --compress --verbose

# Check language support
# Verify file extensions are recognized
```

## Best Practices

1. **Start compressed** for large codebases, expand as needed
2. **Use hybrid approach** for mixed use cases
3. **Combine with filtering** for maximum efficiency
4. **Preserve comments** when documentation is important
5. **Don't compress** when implementation details matter
6. **Verify compression** with token counts before extensive analysis
7. **Re-pack without compression** for detailed investigation
8. **Use compression** for initial exploration, disable for deep work
9. **Compress dependencies**, keep main code full
10. **Leverage compression** with large remote repositories

## Compression Metrics

Track compression effectiveness:

```bash
# Show token counts
repomix --verbose

# Output includes:
# - Original size
# - Compressed size
# - Reduction percentage
# - Token count per file
```

Example output:
```
Processing complete:
- Total files: 150
- Original tokens: 45,000
- Compressed tokens: 13,500
- Reduction: 70%
```

## Future Enhancements

Tree-sitter compression continues to improve:
- Better language support
- More granular control
- Context-aware compression
- Semantic preservation improvements
