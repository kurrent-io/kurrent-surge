# Filter Expressions Specification

**Version:** 2.0.0
**Status:** Implemented
**Last Updated:** 2026-04-15

## Overview

Filter Expressions provide a SQL-like query language for filtering `Record` instances in KurrentDB streams. 

The syntax is designed to be compatible with DuckDB SQL, enabling future optimization where filter expressions can be executed directly against the underlying index storage engine.

### Goals

1. **DuckDB Compatibility**: Syntax matches DuckDB SQL WHERE clause syntax (validated by automated tests against real DuckDB)
2. **Type Safety**: Automatic type coercion following SQL semantics
3. **Performance**: Efficient evaluation for high-volume filtering scenarios
4. **Extensibility**: Easy to add new operators and column support

### Non-Goals

1. Subqueries or joins
2. Aggregation functions
3. Arithmetic expressions
4. User-defined functions

---

## API

### Core Types

```csharp
public sealed class ExpressionFilterParser {
    Result<IFilterExpression, ExpressionFilterParseError> Parse(ReadOnlySpan<char> input);
}

public interface IFilterExpression {
    bool IsMatch(Record record);
}
```

### RecordFilter (Public API)

```csharp
// Create filter from expression
var filter = RecordFilter.ByExpression("stream = 'orders' AND metadata['amount'] > 100");

// Check if records match
bool matches = filter.IsMatch(record);

// Access filter properties (ExpressionFilter)
if (filter is ExpressionFilter exprFilter) {
    Console.WriteLine($"Target stream: {exprFilter.TargetStream}");
    Console.WriteLine($"Expression: {exprFilter.Expression}");
}
```

**Factory Methods:**
- `RecordFilter.None` - Matches all records (no filtering)
- `RecordFilter.ByStream(stream)` - Match a specific stream exactly
- `RecordFilter.ByStreamPrefix(prefixes)` - Match streams by prefix patterns
- `RecordFilter.BySchema(schema)` - Match a specific schema (event type) exactly
- `RecordFilter.BySchemaPrefix(prefixes)` - Match schemas by prefix patterns
- `RecordFilter.ByExpression(expression)` - Full expression with automatic server/client splitting

---

## Schema

Filter expressions evaluate against records with the following virtual schema:

### Implemented Columns

Matches the official `kdb.records` DuckDB schema:

| Column            | Type    | Description                              | Aliases              |
|-------------------|---------|------------------------------------------|----------------------|
| `record_id`       | BLOB    | Unique record identifier                 | -                    |
| `log_position`    | BIGINT  | Commit position in the transaction log   | `position`, `pos`    |
| `created_at`      | BIGINT  | Record append time (Unix millis)         | `timestamp`, `ts`    |
| `stream`          | VARCHAR | Stream name                              | -                    |
| `stream_revision` | BIGINT  | Revision within stream                   | `revision`           |
| `schema_name`     | VARCHAR | Schema/event type name                   | `schema`             |
| `category`        | VARCHAR | Stream category (before first `-`)       | -                    |
| `schema_id`       | VARCHAR | Schema version identifier                | -                    |
| `schema_format`   | VARCHAR | Data format (json, protobuf, avro, bytes)| -                    |
| `data`            | JSON    | Record payload (JSON format only)        | -                    |
| `metadata`        | JSON    | Custom event metadata key-value pairs    | `properties`, `props`|

All aliases are case-insensitive.

### Planned Columns (Not Yet Implemented)

| Column          | Type    | Description                               | Aliases  |
|-----------------|---------|-------------------------------------------|----------|
| `sequence_id`   | UINT64  | Sequential number in operation context    | `seq`    |
| `redacted`      | BOOL    | Whether record is redacted                | -        |
| `deleted`       | BOOL    | Whether record is deleted                 | -        |

---

## Property Access

### Built-in Columns

Direct column access using column name or alias:

```sql
stream = 'orders'
created_at >= 1704067200000
log_position > 1000
category = 'orders'
```

### Custom Metadata

Three syntaxes supported, all DuckDB compatible:

**Bracket Notation** (preferred for keys with special characters):
```sql
metadata['userId'] = 'user-123'
metadata['order.total'] > 100
```

**Dot Notation** (convenient for simple keys):
```sql
metadata.userId = 'user-123'
metadata.amount > 100
```

**Arrow Text Notation** (`->>`, extracts as text):
```sql
metadata->>'userId' = 'user-123'
metadata->>'amount' > 100
```

### JSON Payload Access (`data`)

The `data` column provides access to the record's JSON payload when `schema_format` is JSON.
Returns `null` for non-JSON records or when the record is not decoded.
Nested paths work via dot notation:

```sql
data.address.city = 'NYC'
data.user.profile.name = 'Alice'
data.tags IS NOT NULL
```

Arrow text notation also works for top-level access:
```sql
data->>'region' = 'us-east'
data->>'amount' > '100'
```

### Unsupported: `->` (Single Arrow)

The `->` operator is not supported. It returns JSON type, which is only useful for chained
traversal (`data->'a'->'b'`). Since `->>` and dot notation cover all filter use cases,
there is no reason to add it.

### Property Resolution

Property access is resolved through `PropertyMap<T>` with the `DuckDb` path parser:

1. **Direct lookup** — exact name match against registered aliases (case-insensitive)
2. **Bracket notation** — `name['key']` extracts key from brackets
3. **Dot notation** — `name.key` splits right-to-left on `.` to find registered prefix + key
4. **Trim fallback** — strips configurable prefix/suffix and retries (used for `field_` WebUI compat)

---

## Operators

### Comparison Operators

| Operator | Description             | Example                                  |
|----------|-------------------------|------------------------------------------|
| `=`      | Equality                | `stream = 'orders'`                      |
| `!=`     | Not equal               | `schema_name != 'SystemEvent'`           |
| `<>`     | Not equal (alternative) | `stream <> 'test'`                       |
| `>`      | Greater than            | `metadata['amount'] > 100`               |
| `<`      | Less than               | `metadata['count'] < 50`                 |
| `>=`     | Greater than or equal   | `created_at >= 1704067200000`            |
| `<=`     | Less than or equal      | `metadata['score'] <= 95`                |

### Logical Operators

| Operator | Description | Example                                                                       |
|----------|-------------|-------------------------------------------------------------------------------|
| `AND`    | Logical AND | `stream = 'orders' AND schema_name = 'OrderCreated'`                          |
| `OR`     | Logical OR  | `schema_name = 'OrderCreated' OR schema_name = 'OrderUpdated'`                |
| `NOT`    | Logical NOT | `NOT (stream = 'test')`                                                       |
| `()`     | Grouping    | `(stream = 'orders' OR stream = 'payments') AND metadata['userId'] = '123'`   |

### Pattern Matching Operators

| Operator    | Description                    | Example                       |
|-------------|--------------------------------|-------------------------------|
| `LIKE`      | Case-sensitive pattern match   | `stream LIKE 'order%'`        |
| `NOT LIKE`  | Negated case-sensitive match   | `stream NOT LIKE '%test%'`    |
| `ILIKE`     | Case-insensitive pattern match | `schema_name ILIKE '%event%'` |
| `NOT ILIKE` | Negated case-insensitive match | `stream NOT ILIKE '%TEST%'`   |

**Pattern Wildcards**:
- `%` - Matches any sequence of characters (including empty)
- `_` - Matches any single character

### Null Checking Operators

| Operator      | Description                    | Example                               |
|---------------|--------------------------------|---------------------------------------|
| `IS NULL`     | Check if value is null/missing | `metadata['correlationId'] IS NULL` |
| `IS NOT NULL` | Check if value exists          | `metadata['userId'] IS NOT NULL`    |

### Set Membership Operators

| Operator | Description      | Example                                                |
|----------|------------------|--------------------------------------------------------|
| `IN`     | Value in set     | `stream IN ('orders', 'payments', 'refunds')`          |
| `NOT IN` | Value not in set | `schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')` |

### Range Operators

| Operator      | Description                | Example                                           |
|---------------|----------------------------|---------------------------------------------------|
| `BETWEEN`     | Value in range (inclusive) | `created_at BETWEEN 1704067200000 AND 1735689600000` |
| `NOT BETWEEN` | Value outside range        | `log_position NOT BETWEEN 100 AND 200`               |

---

## Operator Precedence

From lowest to highest:

| Level | Operators                             |
|-------|---------------------------------------|
| 1     | `OR`                                  |
| 2     | `AND`                                 |
| 3     | `NOT`                                 |
| 4     | `IS [NOT] NULL`                       |
| 5     | `BETWEEN`, `IN`, `LIKE`, `ILIKE`      |
| 6     | `=`, `!=`, `<>`, `<`, `>`, `<=`, `>=` |

Use parentheses to override precedence:
```sql
-- Without parentheses: AND binds tighter than OR
stream = 'orders' OR stream = 'payments' AND metadata['amount'] > 100
-- Equivalent to: stream = 'orders' OR (stream = 'payments' AND metadata['amount'] > 100)

-- With parentheses: explicit grouping
(stream = 'orders' OR stream = 'payments') AND metadata['amount'] > 100
```

---

## Data Types

### String Literals

- Enclosed in single quotes: `'value'`
- Escape single quote with backslash: `'O\'Reilly'`
- Case-insensitive comparison by default

```sql
stream = 'orders'
metadata['name'] = 'John O\'Reilly'
```

### Numeric Literals

- Integers: `42`, `-10`, `0`
- Decimals: `99.99`, `-12.5`, `0.001`

```sql
metadata['amount'] > 100
metadata['rate'] >= 0.05
log_position > 1000000
```

### DateTime Literals

- Unix milliseconds (integer): `1704067200000`
- Stored as `BIGINT` in the schema; compare using integer literals

```sql
created_at >= 1704067200000
created_at BETWEEN 1704067200000 AND 1735689600000
```

### Boolean Literals

- Keywords: `true`, `false` (case-insensitive)
- Also accepts strings: `'true'`, `'false'`

```sql
metadata['isVip'] = true
metadata['active'] = 'true'
```

### NULL

- Keyword: `NULL` (case-insensitive)
- Use `IS NULL` / `IS NOT NULL` for comparison

```sql
metadata['correlationId'] IS NULL
metadata['userId'] IS NOT NULL
```

---

## Type Coercion

The filter system performs automatic type coercion following SQL semantics:

### String Comparisons

- Case-insensitive by default
- Collation: ordinal (byte-by-byte after case folding)

### Numeric Comparisons

- Automatic promotion to widest type
- Hierarchy: `decimal` > `double` > `float` > `long` > `int`

```sql
-- String '100' compared to integer 100: string is parsed as number
metadata['amount'] > 100  -- works if amount is stored as string "150"
```

### DateTime Comparisons

- `created_at` is stored as Unix milliseconds (`BIGINT`); compare using integer literals
- No string-to-date coercion — use numeric millis directly

### NULL Handling

- `NULL = NULL` returns `false` (SQL standard)
- `NULL IS NULL` returns `true`
- Any comparison with `NULL` returns `false`

---

## Error Handling

Parse errors follow DuckDB format:

```
Error parsing filter: syntax error at or near "TOKEN"
```

### Common Errors

| Error            | Example            | Message                                  |
|------------------|--------------------|------------------------------------------|
| Unexpected token | `stream = AND`     | `syntax error at or near "AND"`          |
| Unclosed string  | `stream = 'orders` | `syntax error at or near "'"`            |
| Missing operand  | `stream =`         | `syntax error at or near "end of input"` |
| Invalid operator | `stream == 'test'` | `syntax error at or near "="`            |
| Single arrow     | `data->'field'`    | `Use '->>' instead of '->'`             |
| Unknown column   | `invalid_col = 1`  | Evaluates to `NULL`, no parse error      |

---

## Examples

### Basic Filtering

```sql
-- Simple equality
stream = 'user-events'

-- Multiple conditions
stream = 'orders' AND metadata['amount'] > 100

-- Complex grouping
(stream = 'orders' OR stream = 'refunds') AND metadata['userId'] = 'user-123'
```

### Pattern Matching

```sql
-- Starts with
stream LIKE 'order%'

-- Contains (case-insensitive)
schema_name ILIKE '%event%'

-- Single character wildcard
stream LIKE 'order_'
```

### Date Range Queries

```sql
-- Specific range
created_at BETWEEN 1704067200000 AND 1706745600000

-- After date
created_at >= 1704067200000

-- Before date
created_at < 1735689600000
```

### Metadata Filtering

```sql
-- Bracket notation
metadata['correlationId'] IS NOT NULL
metadata['userId'] = 'user-123' AND metadata['amount'] > 50
metadata['region'] IN ('us-east', 'us-west', 'eu-west')

-- Dot notation
metadata.correlationId IS NOT NULL
metadata.userId = 'user-123'

-- Arrow text notation
metadata->>'userId' = 'user-123'
```

### Complex Business Logic

```sql
stream = 'orders' AND
metadata['amount'] > 100 AND
metadata['region'] IN ('us-east', 'us-west') AND
metadata['customerType'] != 'internal' AND
created_at >= 1704067200000
```

---

## Limitations

### Current Limitations

- No arithmetic expressions (`amount * 1.1 > 100`)
- No functions (`LOWER(stream) = 'orders'`)
- No subqueries
- No aggregations
- No `->` (single arrow) — use `->>` or dot notation instead
- No array indexing (`data.items[0]`) — bracket notation is for string keys only
- String comparisons are case-insensitive by default
- String escaping uses `\'` (C-style), not `''` (SQL-standard) — expressions using `\'` work in our parser but not in DuckDB
- `data` column not yet registered in `RecordPropertyMap` (parser supports the syntax, evaluator pending)

### Future Enhancements

- `data` column registration in `RecordPropertyMap` for JSON payload access
- Array indexing (`data.items[0]`)
- `GLOB` operator for shell-style pattern matching
- `SIMILAR TO` for SQL standard regex
- Custom functions (e.g., `LOWER`, `UPPER`, `TRIM`)
- Arithmetic expressions
- Configurable case sensitivity
- SQL-standard string escaping (`''`)

---

## Performance Considerations

1. **Index-optimizable filters**: Filters on `stream`, `schema_name`, `created_at`, and `log_position` can potentially be optimized for server-side execution.

2. **Memory filtering**: Filters on `metadata` require in-memory evaluation after records are retrieved.

3. **Pattern matching**: `LIKE`/`ILIKE` patterns are compiled to regex once per expression and cached.

4. **Short-circuit evaluation**: `AND`/`OR` operators use short-circuit evaluation.

5. **Zero-allocation property resolution**: `PropertyResult<T>` is a readonly struct. Numeric comparisons use `Is<T>()` to avoid boxing.

---

## Security Considerations

1. **Regex DoS protection**: LIKE patterns are converted to simple regex without catastrophic backtracking
2. **No code execution**: Filter expressions are data-only AST, no dynamic code generation
3. **Input validation**: All inputs are validated and sanitized during tokenization
4. **Thread-safe**: Parser is stateless and safe for concurrent use

---

## Architecture

### PropertyMap Resolution Pipeline

```
Expression string
  → Tokenizer (lexical analysis)
  → Pratt Parser (AST construction)
  → PropertyAccess(name, key)
  → ExpressionFilterResolver.Find(property, record)
    → PropertyMap<Record>.Find(name, key, trimPrefix: "field_")
      → Direct lookup (FrozenDictionary)
      → IPathParser.TryParse (DuckDb: bracket then dot)
      → TrimName fallback (prefix/suffix stripping)
    → PropertyResult<Record>
      → .Value (boxed) or .Is<T>() / .TryGetDecimal() (unboxed)
```

### Key Types

| Type | Purpose |
|---|---|
| `PropertyMap<T>` | Immutable property resolution engine with pluggable path parsing |
| `PropertyResult<T>` | Lightweight struct for zero-alloc typed property access |
| `IPathParser` | Strategy for parsing property paths into (name, key) candidates |
| `PathParsers.DuckDb` | Default parser: bracket + dot notation (DuckDB compatible) |
| `RecordPropertyMap` | Pre-built `PropertyMap<Record>` with standard column aliases |
| `ExpressionFilterResolver` | Thin wrapper adding `field_` prefix trim for WebUI compat |

---

## Testing

### DuckDB Compatibility Validation

Filter expressions are validated against DuckDB to ensure syntax compatibility. The test creates an in-memory DuckDB database with a table schema matching `Record` fields:

```sql
CREATE TABLE records (
    log_position BIGINT NOT NULL,
    commit_position BIGINT,
    stream_revision BIGINT NOT NULL,
    created_at BIGINT NOT NULL,
    expires_at BIGINT,
    stream VARCHAR NOT NULL,
    stream_hash UBIGINT NOT NULL,
    schema_name VARCHAR NOT NULL,
    category VARCHAR NOT NULL,
    deleted BOOLEAN NOT NULL,
    schema_id VARCHAR,
    schema_format VARCHAR NOT NULL,
    record_id BLOB NOT NULL,
    data JSON,
    metadata JSON
)
```

Each expression is validated using `EXPLAIN SELECT * FROM records WHERE {expression}` to verify it produces valid DuckDB SQL without requiring actual data.

### DuckDB Validated Syntax

| Syntax | DuckDB Column Type | Example |
|---|---|---|
| Bracket notation | JSON | `metadata['region'] = 'us-east'` |
| Dot notation | JSON | `metadata.region = 'us-east'`, `data.address.city = 'NYC'` |
| Arrow text `->>` | JSON | `data->>'region' = 'us-east'` |
| Arrow text with JSONPath | JSON | `data->>'$.region' = 'us-east'` |

### Test Coverage

- **Parser tests**: All syntax constructs parse correctly
- **Evaluation tests**: Filter matching against Record instances
- **DuckDB validation tests**: Expressions validated as real DuckDB SQL
- **PropertyMap tests**: Path resolution, bracket/dot/trim, typed access, numeric coercion
- **Server filter extractor tests**: Server/client filter splitting

---

## References

- [DuckDB Comparison Operators](https://duckdb.org/docs/sql/expressions/comparison_operators)
- [DuckDB Pattern Matching](https://duckdb.org/docs/sql/functions/pattern_matching)
- [DuckDB IN Operator](https://duckdb.org/docs/sql/expressions/in)
- [DuckDB JSON Overview](https://duckdb.org/docs/current/data/json/overview)
- [DuckDB JSON Functions](https://duckdb.org/docs/current/data/json/json_functions)
