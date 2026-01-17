# Record Filter

## Overview

Filter expressions provide a SQL-like query language for filtering records in KurrentDB streams. The filter system supports both basic and advanced querying
capabilities with stream optimization for high-performance filtering.

The idea behind this is not only have a powerful filtering language, but also to be able to optimize the filtering process when possible by actually passing the
queries to the underlying index storage engine - DuckDB.

This allows for very fast filtering of records when the filter expression can be optimized.

Unfortunaltely, to handle filtering of properties (key-value pairs) and data (json payloads), we need to do some in-memory filtering as well, 
since DuckDB requires a specific sintax for querying json data.

Nonetheles, the experience should be as close as to query a table with the following schema:

| Column            | Type     | Description                                                             |
|-------------------|----------|-------------------------------------------------------------------------|
| `record_id`       | string   | Record unique id                                                        |
| `position`        | number   | Record log position                                                     |
| `timestamp`       | datetime | Record timestamp in ISO8601 format                                      |
| `sequence_id`     | number   | A sequencial number, only valid for the current operation context       |
| `redacted`        | bool     | If the record is redacted                                               |
| `stream`          | string   | Stream name                                                             |
| `stream_revision` | number   | Stream revision                                                         |
| `schema_name`     | string   | Schema name                                                             |
| `schema_format`   | string   | Schema data format (protobuf, json, avro, bytes)                        |
| `schema_id`       | string   | Schema version id                                                       |
| `properties`      | map      | Custom metadata fields (key-value pairs)                                |
| `data`            | json     | Record data containing the message/payload (only for json encoded data) |

## Supported Aliases

To make it easier to write filter expressions, several aliases are supported.

| Column            | Alias      |
|-------------------|------------|
| `record_id`       | `id`       |
| `position`        | `pos`      |
| `timestamp`       | `ts`       |
| `sequence_id`     | `seq`      |
| `redacted`        |            |
| `stream`          |            |
| `stream_revision` | `revision` |
| `schema_name`     | `schema`   |
| `schema_format`   | `format`   |
| `schema_id`       |            |
| `properties`      | `props`    |
| `data`            |            |

## Custom Property Access

For accessing custom record properties we use the prefix `properties.` or `props.`.

The format is `properties['{key}']` or `props.['{key}']`.

For now, the value will be treated as a string, number or boolean.

Later on we might support Json struct as a value improving the querying capabilities.

Examples:
- `properties['userId'] = 'user-123'`
- `properties['amount'] > 100`
- `properties['isVip'] = true`

### Data Filtering

For data filtering, the regular paths are not case sensitive.
The format is `data.{path}`.

But we also support arrow notation for accessing json properties.

The format is `data->{path}` for json values
or `data->>'{path}'` for string values.

this is pure duckdb syntax.

Examples using duckdb arrow notation:
- `data->>'userId' = 'user-123'`
- `(data->>'amount')::INTEGER > 100`
- `data->'address'->>'city' = 'NY'`

Same Examples using dot notation:
- `data.userId = 'user-123'`
- `data.amount > 100`
- `data.address.city = 'NY'`

## Current Capabilities

### Comparison Operators

| Operator | Description                                | Example                               |
|----------|--------------------------------------------|---------------------------------------|
| `=`      | Equality                                   | `stream = 'orders'`                   |
| `!=`     | Not equal                                  | `schema.name != 'SystemEvent'`        |
| `<>`     | Not equal (alternative)                    | `stream <> 'test'`                    |
| `>`      | Greater than                               | `metadata.amount > 100`               |
| `<`      | Less than                                  | `metadata.count < 50`                 |
| `>=`     | Greater than or equal                      | `timestamp >= '2024-01-01T00:00:00Z'` |
| `<=`     | Less than or equal                         | `metadata.score <= 95`                |
| `~`      | Case-insensitive contains (string)         | `metadata.description ~ 'urgent'`     |
| `~*`     | Case-sensitive contains (string)           | `metadata.description ~* 'Important'` |
| `!~`     | Case-insensitive does not contain (string) | `metadata.note !~ 'deprecated'`       |
| `!~*`    | Case-sensitive does not contain (string)   | `metadata.note !~* 'Obsolete'`        |

### Logical Operators

| Operator | Description | Example                                                                  |
|----------|-------------|--------------------------------------------------------------------------|
| `AND`    | Logical AND | `stream = 'orders' AND schema.name = 'OrderCreated'`                     |
| `OR`     | Logical OR  | `schema.name = 'OrderCreated' OR schema.name = 'OrderUpdated'`           |
| `()`     | Grouping    | `(stream = 'orders' OR stream = 'payments') AND metadata.userId = '123'` |

### Null Checking Operators (New)

| Operator      | Description                    | Example                          |
|---------------|--------------------------------|----------------------------------|
| `IS NULL`     | Check if value is null/missing | `metadata.correlationId IS NULL` |
| `IS NOT NULL` | Check if value exists          | `metadata.userId IS NOT NULL`    |

### Set Membership Operators (New)

| Operator | Description      | Example                                                |
|----------|------------------|--------------------------------------------------------|
| `IN`     | Value in set     | `stream IN ('orders', 'payments', 'refunds')`          |
| `NOT IN` | Value not in set | `schema.name NOT IN ('SystemEvent', 'HeartbeatEvent')` |

## Data Types

### String Literals

- Must be enclosed in single quotes: `'value'`
- Supports escaped quotes: `'O\'Reilly'`
- Case-insensitive comparisons by default

### Numeric Literals

- Integers: `42`, `-10`
- Decimals: `99.99`, `-12.5`
- Scientific notation: Not supported

### DateTime Literals

- ISO 8601 format: `'2024-01-15T10:30:00Z'`
- With timezone: `'2024-01-15T10:30:00+02:00'`
- Date only: `'2024-01-15'`

### Boolean Values

- Represented as strings: `'true'`, `'false'`
- Case-insensitive: `'TRUE'`, `'False'`

## Stream Optimization

The filter system includes intelligent stream analysis for performance optimization:

### Single-Stream Optimization

When a filter targets a single specific stream, the query can be optimized:

```sql
-- Optimizable (targets single stream)
stream = 'orders'
stream = 'orders' AND schema.name = 'OrderCreated'
(stream = 'orders' AND schema.name = 'OrderCreated') OR (stream = 'orders' AND schema.name = 'OrderUpdated')
```

### Non-Optimizable Filters

These filters cannot be optimized for single-stream reads:

```sql
-- Cannot optimize (no stream filter)
schema.name = 'OrderCreated'
metadata.userId = 'user-123'

-- Cannot optimize (multiple streams)
stream = 'orders' OR stream = 'payments'
stream IN ('orders', 'payments', 'refunds')

-- Cannot optimize (exclusion only)
stream != 'system-events'
```

## Examples

### Basic Filtering

```sql
-- Simple equality
stream = 'user-events'

-- Multiple conditions
stream = 'orders' AND metadata.amount > 100

-- Complex grouping
(stream = 'orders' OR stream = 'refunds') AND metadata.userId = 'user-123'
```

### Advanced Filtering

```sql
-- Null checking
metadata.correlationId IS NOT NULL AND stream = 'orders'

-- Set membership
stream IN ('orders', 'payments') AND schema.name NOT IN ('SystemEvent', 'TestEvent')

-- Date range filtering
timestamp >= '2024-01-01T00:00:00Z' AND timestamp < '2024-02-01T00:00:00Z'

-- Complex business logic
stream = 'orders' AND 
metadata.amount > 100 AND 
metadata.region IN ('us-east', 'us-west') AND 
metadata.customerType != 'internal'
```

## Safety & Limitations

### Security

- **Stack Overflow Protection**: Maximum nesting depth of 10 levels
- **Thread Safety**: Parser is thread-safe (new instance per parse)
- **Input Validation**: All inputs are validated and sanitized

### Performance Considerations

- Case-insensitive string comparisons may impact performance with large datasets
- Complex expressions with many OR conditions should be avoided in hot paths
- Stream-optimized filters perform significantly better than general filters

### Current Limitations

- No support for nested metadata paths (e.g., `metadata.user.profile.age`)
- No LIKE operator for pattern matching
- No BETWEEN operator for range queries
- No support for arithmetic expressions
- No support for functions or aggregations

## Error Handling

The filter system provides comprehensive error reporting:

### Common Errors

- **Empty Expression**: `Expression cannot be empty`
- **Invalid Property**: `Invalid property path 'invalid.field'. Valid paths: stream, schema.name, schema.version, timestamp, metadata.*`
- **Multiple Streams**: `Filter cannot target multiple streams: 'orders', 'payments'`
- **Nesting Too Deep**: `Expression exceeds maximum nesting depth of 10`
- **Unbalanced Parentheses**: `Expected ) but found END at position 25`
- **Invalid Operator**: `Expected comparison operator (=, !=, <>, >, <, >=, <=, IS NULL, IS NOT NULL, IN) after property 'stream'`

### Best Practices

1. **Start Simple**: Begin with basic equality filters before adding complexity
2. **Use Stream Optimization**: When possible, include stream equality for better performance
3. **Validate Early**: Test your expressions with sample data before production use
4. **Handle Errors Gracefully**: Always check `ParseResult.IsValid` before using expressions
5. **Cache Compiled Expressions**: Reuse parsed expressions when filtering similar datasets

## Migration Notes

### From Previous Versions

- All existing filter expressions remain compatible
- New operators (`IS NULL`, `IS NOT NULL`, `IN`, `NOT IN`) are additive enhancements
- Error messages are more descriptive and helpful

### Future Enhancements (Planned)

- Expression compilation and caching for better performance
- Support for nested metadata paths
- LIKE operator for pattern matching
- BETWEEN operator for range queries
- Custom function support
- Configurable case sensitivity

## API Usage

### Basic Usage

```csharp
// Create a filter expression
var result = FilterExpression.Create("stream = 'orders' AND metadata.amount > 100");

if (result.IsValid) {
    // Use the compiled expression
    var matchingRecords = records.Where(r => result.Expression.Evaluate(r));
    
    // Check if expression can be optimized
    if (result.CanOptimizeForStream) {
        Console.WriteLine($"Can optimize for stream: {result.TargetStream}");
    }
} else {
    // Handle parsing errors
    foreach (var error in result.Errors) {
        Console.WriteLine($"Error: {error}");
    }
}
```

### Advanced Usage

```csharp
// Create filter with null checking and set membership
var complexFilter = FilterExpression.Create(@"
    stream IN ('orders', 'refunds') AND 
    metadata.userId IS NOT NULL AND 
    metadata.amount > 50 AND 
    schema.name NOT IN ('TestEvent', 'SystemEvent')
");

// Evaluate against records
var filteredRecords = records
    .Where(record => complexFilter.Expression?.Evaluate(record) ?? false)
    .ToList();
```
