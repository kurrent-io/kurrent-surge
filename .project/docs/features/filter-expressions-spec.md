# Filter Expressions Specification

**Version:** 1.2.0
**Status:** Implemented
**Last Updated:** 2025-11-19

## Overview

Filter Expressions provide a SQL-like query language for filtering `Record` instances in KurrentDB streams. 

The syntax is designed to be compatible with DuckDB SQL, enabling future optimization where filter expressions can be executed directly against the underlying index storage engine.

### Goals

1. **DuckDB Compatibility**: Syntax matches DuckDB SQL WHERE clause syntax (validated by automated tests)
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
var filter = RecordFilter.ByExpression("stream = 'orders' AND properties.amount > 100");

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

### Usage Example

```csharp
var parser = new ExpressionFilterParser();
var result = parser.Parse("stream = 'orders' AND properties['amount'] > 100");

if (result.IsSuccess) {
    var filter = result.Value;

    foreach (var record in records) {
        if (filter.IsMatch(record)) {
            // Process matching record
        }
    }
}
```

---

## Schema

Filter expressions evaluate against records with the following virtual schema:

### Implemented Columns

| Column            | Type     | Description                     | Aliases    |
|-------------------|----------|---------------------------------|------------|
| `record_id`       | GUID     | Unique record identifier        | `id`       |
| `position`        | INT64    | Log position (commit position)  | `pos`      |
| `timestamp`       | DATETIME | Record creation timestamp       | `ts`       |
| `stream`          | STRING   | Stream name                     | -          |
| `stream_revision` | INT64    | Revision within stream          | `revision` |
| `schema_name`     | STRING   | Schema/event type name          | `schema`   |
| `properties`      | MAP      | Custom metadata key-value pairs | `props`    |

### Planned Columns (Not Yet Implemented)

| Column          | Type   | Description                               | Aliases  |
|-----------------|--------|-------------------------------------------|----------|
| `sequence_id`   | UINT64 | Sequential number in operation context    | `seq`    |
| `redacted`      | BOOL   | Whether record is redacted                | -        |
| `schema_format` | STRING | Data format (json, protobuf, avro, bytes) | `format` |
| `schema_id`     | GUID   | Schema version identifier                 | -        |
| `data`          | JSON   | Record payload (JSON format only)         | -        |

---

## Property Access

### Built-in Columns

Direct column access using column name:

```sql
stream = 'orders'
timestamp >= '2024-01-01'
position > 1000
```

### Custom Properties (Metadata)

Two syntaxes supported for accessing custom metadata:

**Bracket Notation** (preferred for keys with special characters):
```sql
properties['userId'] = 'user-123'
properties['order.total'] > 100
props['correlationId'] IS NOT NULL
```

**Dot Notation** (convenient for simple keys):
```sql
properties.userId = 'user-123'
properties.amount > 100
props.correlationId IS NOT NULL
```

### Data Field Access (Not Yet Implemented)

Data field access for JSON payloads is planned for a future release. This will support:
- Dot notation: `data.userId = 'user-123'`
- Arrow notation: `data->>'userId' = 'user-123'`

---

## Operators

### Comparison Operators

| Operator | Description             | Example                               |
|----------|-------------------------|---------------------------------------|
| `=`      | Equality                | `stream = 'orders'`                   |
| `!=`     | Not equal               | `schema_name != 'SystemEvent'`        |
| `<>`     | Not equal (alternative) | `stream <> 'test'`                    |
| `>`      | Greater than            | `properties.amount > 100`             |
| `<`      | Less than               | `properties.count < 50`               |
| `>=`     | Greater than or equal   | `timestamp >= '2024-01-01T00:00:00Z'` |
| `<=`     | Less than or equal      | `properties.score <= 95`              |

### Logical Operators

| Operator | Description | Example                                                                    |
|----------|-------------|----------------------------------------------------------------------------|
| `AND`    | Logical AND | `stream = 'orders' AND schema_name = 'OrderCreated'`                       |
| `OR`     | Logical OR  | `schema_name = 'OrderCreated' OR schema_name = 'OrderUpdated'`             |
| `NOT`    | Logical NOT | `NOT (stream = 'test')`                                                    |
| `()`     | Grouping    | `(stream = 'orders' OR stream = 'payments') AND properties.userId = '123'` |

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
- `\%` - Literal percent sign
- `\_` - Literal underscore

### Null Checking Operators

| Operator      | Description                    | Example                            |
|---------------|--------------------------------|------------------------------------|
| `IS NULL`     | Check if value is null/missing | `properties.correlationId IS NULL` |
| `IS NOT NULL` | Check if value exists          | `properties.userId IS NOT NULL`    |

### Set Membership Operators

| Operator | Description      | Example                                                |
|----------|------------------|--------------------------------------------------------|
| `IN`     | Value in set     | `stream IN ('orders', 'payments', 'refunds')`          |
| `NOT IN` | Value not in set | `schema_name NOT IN ('SystemEvent', 'HeartbeatEvent')` |

### Range Operators

| Operator      | Description                | Example                                           |
|---------------|----------------------------|---------------------------------------------------|
| `BETWEEN`     | Value in range (inclusive) | `timestamp BETWEEN '2024-01-01' AND '2024-12-31'` |
| `NOT BETWEEN` | Value outside range        | `position NOT BETWEEN 100 AND 200`                |

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
stream = 'orders' OR stream = 'payments' AND properties.amount > 100
-- Equivalent to: stream = 'orders' OR (stream = 'payments' AND properties.amount > 100)

-- With parentheses: explicit grouping
(stream = 'orders' OR stream = 'payments') AND properties.amount > 100
```

---

## Data Types

### String Literals

- Enclosed in single quotes: `'value'`
- Escape single quote with backslash: `'O\'Reilly'`
- Case-insensitive comparison by default

```sql
stream = 'orders'
properties.name = 'John O\'Reilly'
```

### Numeric Literals

- Integers: `42`, `-10`, `0`
- Decimals: `99.99`, `-12.5`, `0.001`

```sql
properties.amount > 100
properties.rate >= 0.05
position > 1000000
```

### DateTime Literals

- ISO 8601 format in single quotes
- With timezone: `'2024-01-15T10:30:00+02:00'`
- UTC: `'2024-01-15T10:30:00Z'`
- Date only: `'2024-01-15'`

```sql
timestamp >= '2024-01-01T00:00:00Z'
timestamp BETWEEN '2024-01-01' AND '2024-12-31'
```

### Boolean Literals

- Keywords: `true`, `false` (case-insensitive)
- Also accepts strings: `'true'`, `'false'`

```sql
properties.isVip = true
properties.active = 'true'
```

### NULL

- Keyword: `NULL` (case-insensitive)
- Use `IS NULL` / `IS NOT NULL` for comparison

```sql
properties.correlationId IS NULL
properties.userId IS NOT NULL
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
properties.amount > 100  -- works if amount is stored as string "150"
```

### DateTime Comparisons

- Automatic conversion between `DateTime` and `DateTimeOffset`
- String literals parsed as ISO 8601

### NULL Handling

- `NULL = NULL` returns `false` (SQL standard)
- `NULL IS NULL` returns `true`
- Any comparison with `NULL` returns `false`
- `IN` with `NULL` in set: uses tuple semantics (returns `NULL` if not found and set contains `NULL`)

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
| Unknown column   | `invalid_col = 1`  | Evaluates to `NULL`, no parse error      |

---

## Examples

### Basic Filtering

```sql
-- Simple equality
stream = 'user-events'

-- Multiple conditions
stream = 'orders' AND properties.amount > 100

-- Complex grouping
(stream = 'orders' OR stream = 'refunds') AND properties.userId = 'user-123'
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
timestamp BETWEEN '2024-01-01T00:00:00Z' AND '2024-02-01T00:00:00Z'

-- After date
timestamp >= '2024-01-01'

-- Before date
timestamp < '2024-12-31T23:59:59Z'
```

### Metadata Filtering

```sql
-- Check existence
properties.correlationId IS NOT NULL

-- Multiple metadata conditions
properties.userId = 'user-123' AND properties.amount > 50

-- Set membership
properties.region IN ('us-east', 'us-west', 'eu-west')
```

### Complex Business Logic

```sql
stream = 'orders' AND
properties.amount > 100 AND
properties.region IN ('us-east', 'us-west') AND
properties.customerType != 'internal' AND
timestamp >= '2024-01-01'
```

---

## Limitations

### Current Limitations

- No arithmetic expressions (`amount * 1.1 > 100`)
- No functions (`LOWER(stream) = 'orders'`)
- No subqueries
- No aggregations
- Data field access not yet implemented
- String comparisons are case-insensitive by default

### Future Enhancements

- Data field access (`data.field`, `data->>'field'`)
- `GLOB` operator for shell-style pattern matching
- `SIMILAR TO` for SQL standard regex
- Custom functions (e.g., `LOWER`, `UPPER`, `TRIM`)
- Arithmetic expressions
- Configurable case sensitivity
- Maximum nesting depth protection

---

## Performance Considerations

1. **Index-optimizable filters**: Filters on `stream`, `schema_name`, `timestamp`, and `position` can potentially be optimized for server-side execution.

2. **Memory filtering**: Filters on `properties` require in-memory evaluation after records are retrieved.

3. **Pattern matching**: `LIKE`/`ILIKE` patterns are compiled to regex once per expression and cached.

4. **Short-circuit evaluation**: `AND`/`OR` operators use short-circuit evaluation.

5. **Parser efficiency**: `ExpressionFilterParser` is lightweight and can be instantiated per use or shared.

---

## Security Considerations

1. **Regex DoS protection**: LIKE patterns are converted to simple regex without catastrophic backtracking
2. **No code execution**: Filter expressions are data-only AST, no dynamic code generation
3. **Input validation**: All inputs are validated and sanitized during tokenization
4. **Thread-safe**: Parser is stateless and safe for concurrent use

---

## Testing

### DuckDB Compatibility Validation

Filter expressions are validated against DuckDB to ensure syntax compatibility. The test creates an in-memory DuckDB database with a table schema matching `Record` fields:

```sql
CREATE TABLE events (
    record_id UUID,
    stream VARCHAR,
    schema_name VARCHAR,
    timestamp TIMESTAMPTZ,
    position BIGINT,
    stream_revision BIGINT,
    properties MAP(VARCHAR, VARCHAR)
)
```

Each expression is validated using `EXPLAIN SELECT * FROM events WHERE {expression}` to verify it produces valid DuckDB SQL without requiring actual data.

### Test Coverage

- **Parser tests**: Verify all syntax constructs parse correctly
- **Analyzer tests**: Verify server/client filter splitting
- **Evaluation tests**: Verify filter matching behavior
- **DuckDB validation tests**: Verify expressions are valid DuckDB WHERE clauses

---

## References

- ADR: `.project/docs/adr/0001-filter-expression-parser-architecture.md`
- Original Idea: `.project/reference/ideas/FilterExpressions.md`
- [DuckDB Comparison Operators](https://duckdb.org/docs/sql/expressions/comparison_operators)
- [DuckDB Pattern Matching](https://duckdb.org/docs/sql/functions/pattern_matching)
- [DuckDB IN Operator](https://duckdb.org/docs/sql/expressions/in)
