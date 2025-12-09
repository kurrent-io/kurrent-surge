# ADR-0001: Filter Expression Parser Architecture

* **Status:** Accepted
* **Date:** 2024-11-18
* **Decision Makers:** SILVEIRA

## Context

KurrentDB .NET Client requires a SQL-like filter expression system that allows users to filter records using DuckDB-compatible syntax. The filter system should support:

- Comparison operators (`=`, `!=`, `<>`, `>`, `<`, `>=`, `<=`)
- Logical operators (`AND`, `OR`, `NOT`)
- Pattern matching (`LIKE`, `ILIKE`, `NOT LIKE`, `NOT ILIKE`)
- Null checking (`IS NULL`, `IS NOT NULL`)
- Set membership (`IN (...)`, `NOT IN (...)`)
- Range queries (`BETWEEN x AND y`, `NOT BETWEEN x AND y`)
- Property access (`properties['key']` and `properties.key` syntax)
- Future: Data field access for JSON payloads (`data.field`, `data->>'field'`)

The implementation must work directly with the `Record` type and follow project conventions (K&R brace style, Result pattern, TUnit testing).

## Decision

We will implement **Approach 2: Clean Architecture with Pratt Parser**.

## Alternatives Considered

### Approach 2: Clean Architecture (Selected)

**Strategy**: Fresh implementation with Pratt parsing algorithm, file-scoped types for encapsulation, clean separation between lexer/parser/evaluation layers.

**Key Characteristics**:
- Pratt parser elegantly handles operator precedence without explicit tables
- All AST nodes organized with `file` keyword for encapsulation
- Separate `PropertyAccessExpression` for property resolution
- `ValueComparer` and `PatternMatcher` utility classes
- Direct `Record` type access (non-generic)

**Estimated Files**:
- `IFilterExpressionParser.cs` - Public interface
- `FilterParseError.cs` - Error type
- `DuckDb/DuckDbTokenizer.cs` - Tokenizer
- `DuckDb/DuckDbParser.cs` - Pratt parser
- `DuckDb/DuckDbFilterExpressionParser.cs` - Parser facade
- `Expressions/FilterExpressionNodes.cs` - AST nodes
- `Expressions/PropertyAccessExpression.cs` - Property resolution + utilities

### Approach 3: Performance-Focused (Compiled Expression Trees)

**Strategy**: Compile AST to `Func<Record, bool>` via `System.Linq.Expressions`, achieving native JIT-compiled evaluation with zero allocations during evaluation.

**Key Characteristics**:
- Expression tree compilation (10-100x faster evaluation, ~10-100ns vs ~500-1000ns)
- Zero memory allocations per `IsMatch()` call
- Struct-based tokens, discriminated union AST
- Type coercion resolved at compile time
- One-time compile cost (~1-5ms per expression)

## Rationale

Approach 2 was selected for the following reasons:

1. **Right balance for use case**: This is an isolated, optional feature. The extreme performance of compiled expression trees is unnecessary unless filtering millions of records per second. The interpreted approach is fast enough for typical use cases.

2. **Maintainability**: Pratt parsing is elegant and well-understood. Expression tree APIs (`System.Linq.Expressions`) are complex and require specialized knowledge, making future maintenance harder.

3. **Extensibility**: Adding new operators (e.g., data field access for JSON) is straightforward with Pratt parsing - just add token type and parsing rule. Expression tree compilation requires generating new Expression nodes for each operator.

4. **Debugging**: Interpreted AST nodes are easy to inspect and debug. Compiled delegates are opaque and harder to troubleshoot.

5. **Implementation timeline**: Approach 2 requires 10-13 days vs 15-20 days for Approach 3. The additional complexity of Approach 3 doesn't justify the performance gains for this feature's scope.

6. **Future optimization path**: If performance becomes critical, expression tree compilation can be added as an optimization layer on top of the clean AST without rewriting the parser or property resolution logic.

## Consequences

### Positive

- Clean, maintainable codebase with proper separation of concerns
- File-scoped types keep public API surface minimal
- Extensible for future operators (data field access, GLOB, SIMILAR TO)
- Standard Pratt parsing algorithm is well-documented
- Reasonable evaluation performance for typical workloads

### Negative

- Evaluation performance (~500-1000ns) is slower than compiled approach (~10-100ns)
- Memory allocations during evaluation (though minimal with careful design)
- Virtual dispatch overhead for AST node evaluation

### Neutral

- Existing filter code will be completely replaced
- Tests need to be written from scratch

## Implementation Notes

### Operator Precedence (Pratt Binding Powers)

| Level       | Operators                             |
|-------------|---------------------------------------|
| 1 (lowest)  | `OR`                                  |
| 2           | `AND`                                 |
| 3           | `NOT`                                 |
| 4           | `IS [NOT] NULL`                       |
| 5           | `BETWEEN`, `IN`, `LIKE`, `ILIKE`      |
| 6 (highest) | `=`, `!=`, `<>`, `<`, `>`, `<=`, `>=` |

### Error Format

All parse errors follow DuckDB format:
```
Error parsing filter: syntax error at or near "TOKEN"
```

### Property Resolution

| Syntax              | Resolution                       |
|---------------------|----------------------------------|
| `stream`            | `record.Stream.Value`            |
| `schema_name`       | `record.Schema.SchemaName.Value` |
| `timestamp`         | `record.Timestamp`               |
| `position`          | `record.Position.CommitPosition` |
| `properties['key']` | `record.Metadata.TryGet("key")`  |
| `properties.key`    | `record.Metadata.TryGet("key")`  |

## References

- **Feature Specification**: `.project/reference/features/filter-expressions.md`
- [DuckDB SQL Expressions](https://duckdb.org/docs/sql/expressions/comparison_operators)
- [DuckDB Pattern Matching](https://duckdb.org/docs/sql/functions/pattern_matching)
- [DuckDB IN Operator](https://duckdb.org/docs/sql/expressions/in)
- [Pratt Parsing](https://matklad.github.io/2020/04/13/simple-but-powerful-pratt-parsing.html)
