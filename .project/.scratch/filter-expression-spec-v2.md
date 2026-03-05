# Filter Expressions Specification

**Version:** 2.0.0-draft
**Status:** Draft
**Last Updated:** 2026-02-17
**Previous Version:** 1.2.0 (2025-11-19)

## Overview

Filter Expressions provide a SQL-like query language for filtering records in
KurrentDB. The syntax is designed to be compatible with DuckDB SQL WHERE clauses,
enabling server-side evaluation against the DuckDB-backed secondary index engine.

Version 2.0 extends the language with:

- **FROM clause** for scoping queries to streams, categories, indexes, or the full log
- **Full column alias parity** across client, WebUI, and server `rec` object naming
- **JSON data access** using DuckDB-compatible `->` and `->>` extraction operators
- **`category` column** as a first-class derived field

### Goals

1. **DuckDB Compatibility** - syntax matches DuckDB SQL WHERE clauses
2. **Unified Naming** - one expression works across client API, WebUI, and server
3. **FROM Clause** - scope queries to streams, categories, or indexes
4. **JSON Access** - filter on event payload fields using DuckDB JSON operators
5. **Type Safety** - automatic type coercion following SQL semantics
6. **Extensibility** - easy to add new operators and column support

### Non-Goals

1. SELECT projections (this is a filter language, not a query language)
2. Subqueries or joins
3. Aggregation functions
4. Arithmetic expressions
5. User-defined functions

---

## Grammar

```
filter          := [from_clause] [WHERE] predicate
                 | predicate

from_clause     := FROM source

source          := source_type ':' source_name
                 | ALL | LOG | '$all'

source_type     := STREAM | CATEGORY | INDEX

source_name     := unquoted_name | quoted_name

unquoted_name   := [a-zA-Z0-9_.-]+

quoted_name     := '\'' [^']* '\''

predicate       := or_expr
or_expr         := and_expr (OR and_expr)*
and_expr        := not_expr (AND not_expr)*
not_expr        := NOT not_expr | comparison
comparison      := value_expr compare_op value_expr
                 | value_expr IS [NOT] NULL
                 | value_expr [NOT] IN '(' value_list ')'
                 | value_expr [NOT] BETWEEN value_expr AND value_expr
                 | value_expr [NOT] LIKE string_literal
                 | value_expr [NOT] ILIKE string_literal
                 | '(' predicate ')'
value_expr      := column_ref | json_access | literal
column_ref      := identifier ['.' identifier]* | identifier '[' string_literal ']'
json_access     := value_expr '->' path | value_expr '->>' path
path            := string_literal | '$.' json_path
literal         := string_literal | number | TRUE | FALSE | NULL
```

All keywords (`FROM`, `WHERE`, `AND`, `OR`, `NOT`, `IN`, `BETWEEN`, `LIKE`, `ILIKE`,
`IS`, `NULL`, `TRUE`, `FALSE`, `ALL`, `LOG`, `STREAM`, `CATEGORY`, `INDEX`) are
**case-insensitive**.

---

## Record Schema

Filter expressions evaluate against records with the following virtual schema.
All names in an alias group resolve to the same underlying field.

### Column Reference

| Canonical Name | Aliases                           | Type                | Description                                      |
|----------------|-----------------------------------|---------------------|--------------------------------------------------|
| `id`           | `record_id`                       | GUID                | Unique record identifier                         |
| `stream`       | -                                 | STRING              | Stream name                                      |
| `category`     | -                                 | STRING              | Stream category (derived: text before first `-`) |
| `schema`       | `schema_name`, `event_type`       | STRING              | Schema / event type name                         |
| `revision`     | `stream_revision`, `event_number` | INT64               | Position within the stream                       |
| `position`     | `pos`, `log_position`             | INT64               | Global log position (commit position)            |
| `timestamp`    | `ts`, `created_at`                | DATETIME            | Record creation timestamp                        |
| `properties`   | `props`, `metadata`               | MAP(STRING, STRING) | Custom metadata key-value pairs                  |
| `data`         | -                                 | JSON                | Record payload (JSON format only)                |
| `format`       | `schema_format`                   | STRING              | Data format (json, protobuf, avro, bytes)        |
| `sequence`     | `sequence_id`, `seq`              | UINT64              | Sequential counter in operation context          |
| `redacted`     | -                                 | BOOL                | Whether the record has been redacted             |

Considering, having the `rec.` prefix so we can apply the same convention to the new canonical names. 

This will make it easier to distinguish between the canonical names and the aliases.

RECORDS

| #  | Canonical Name     | Filter Spec          | Aliases: WebUI SQL | Aliases: User Indexes         | Aliases: Connectors           | Aliases: DotNotation | All Aliases                                                              |
|----|--------------------|----------------------|--------------------|-------------------------------|-------------------------------|----------------------|--------------------------------------------------------------------------|
| 1  | `Record ID`        | `record_id`          |                    | `rec.id`                      | `rec.id`                      |                      |                                                                          |
| 2  | `Stream Name`      | `stream`             | `stream`           | `rec.position.stream`         | `rec.position.stream`         |                      | `stream_name`, `rec.stream`, `rec.position.stream`                       |
| 3  | `Category Name`    | `category`           | `category`         | *(derived)*                   | *(derived)*                   |                      | `rec.category`                                                           |
| 4  | `Schema Name`      | `schema_name`        | `event_type`       | `rec.schema.name`             | `rec.schema.name`             |                      | `schema`, `event_type`, `rec.schema.name`                                |
| 5  | `Schema ID`        | `schema_id`          |                    |                               |                               |                      | `rec.schema.id`                                                          |
| 6  | `Stream Revision`  | `stream_revision`    | `event_number`     | `rec.position.streamRevision` | `rec.position.streamRevision` |                      | `revision`,`event_number`, `rec.position.streamRevision`, `rec.revision` |
| 7  | `Log Position`     | `position`           | `log_position`     | `rec.position.logPosition`    | `rec.position.logPosition`    |                      | `pos`, `log_position`, `rec.position.logPosition`                        |
| 8  | `Create Timestamp` | `timestamp`          | `created_at`       | `rec.timestamp`               | `rec.timestamp`               |                      | `ts`, `created_ts`, `created_at`, `rec.timestamp`                        |
| 9  | `Properties`       | `properties`         | `metadata`         | `rec.properties`              | `rec.properties`              |                      | `props`, `metadata`, `rec.properties`                                    |
| 10 | `Data`             | *`data`              |                    |                               |                               |                      | `rec.data`                                                               |
| 11 | `Value`            | * when data is json  | `data`             | `rec.value`                   | `rec.value`                   |                      | `message`, `rec.value`                                                   |
| 12 | `Schema Format`    | *`schema_format`     |                    | `rec.schema.format`           | `rec.schema.format`           |                      | `format`                                                                 |
| 13 | `Sequence Number`  | *`sequence`          |                    | `rec.sequence`                | `rec.sequence`                |                      | `seq`, `rec.sequence`                                                    |
| 14 | `Redacted`         | *`redacted`          |                    | `rec.redacted`                | `rec.redacted`                |                      | `rec.redacted`                                                           |

METADATA (about streams, not individual records)

- FROM metadata sources like `FROM metadata WHERE stream_name` the following additional columns are available for filtering on stream-level metadata. 

| # | Canonical Name        | Filter Spec            | Type     | Aliases: DotNotation   |
|---|-----------------------|------------------------|----------|------------------------|
| 2 | `Stream Name`         | `stream_name`          | STRING   | `stream.name`          |
| 2 | `Stream Revision`     | `stream_revision`      | INT64    | `stream.revision`      |
| 2 | `Create Timestamp`    | `stream_created_at`    | DATETIME | `stream.created_at`    |
| 5 | `Max Age`             | `stream_max_age`       | INT64    | `stream.max_age`       |
| 6 | `Max Size`            | `stream_max_size`      | INT64    | `stream.max_size`      |
| 1 | `Deleted`             | `stream_deleted`       | BOOL     | `stream.deleted`       |
| 2 | `Delete Timestamp`    | `stream_deleted_at`    | DATETIME | `stream.deleted_at`    |
| 3 | `Tombstoned`          | `stream_tombstoned`    | BOOL     | `stream.tombstoned`    |
| 4 | `Tombstone Timestamp` | `stream_tombstoned_at` | DATETIME | `stream.tombstoned_at` |

*** — indicates fields that are not currently available but are planned for future implementation.

### Cross-System Name Mapping

Shows where each alias originates:

| Canonical    | Client Spec v1               | WebUI SQL      | Server `rec` Object           |
|--------------|------------------------------|----------------|-------------------------------|
| `id`         | `record_id` (primary)        | -              | `rec.id`                      |
| `stream`     | `stream`                     | `stream`       | `rec.position.stream`         |
| `category`   | -                            | `category`     | *(derived)*                   |
| `schema`     | `schema_name` (primary)      | `event_type`   | `rec.schema.name`             |
| `revision`   | `stream_revision` (primary)  | `event_number` | `rec.position.streamRevision` |
| `position`   | `position` (primary)         | `log_position` | `rec.position.logPosition`    |
| `timestamp`  | `timestamp` (primary)        | `created_at`   | `rec.timestamp`               |
| `properties` | `properties` (primary)       | `metadata`     | `rec.properties`              |
| `data`       | *(planned)*                  | `data`         | `rec.value`                   |
| `format`     | *(planned)*                  | -              | `rec.schema.format`           |
| `sequence`   | *(planned as `sequence_id`)* | -              | `rec.sequence`                |
| `redacted`   | *(planned)*                  | -              | `rec.redacted`                |

### Category Derivation

`category` is computed from `stream` by extracting text before the first `-`:

- `stream = 'order-abc123'` -> `category = 'order'`
- `stream = 'payment-2026-001'` -> `category = 'payment'`
- `stream = 'nodelimiter'` -> `category = 'nodelimiter'`

---

## FROM Clause - Source Scoping

The optional FROM clause determines which records the predicate filters over.
When omitted, the filter applies to whatever read context the caller provides.

### Source Types

| Source             | Syntax                         | Resolves To                   |
|--------------------|--------------------------------|-------------------------------|
| Specific stream    | `FROM stream:'order-abc123'`   | Direct stream read            |
| Category           | `FROM category:order`          | `$idx-ce-order`               |
| User-defined index | `FROM index:orders-by-country` | `$idx-user-orders-by-country` |
| All events         | `FROM all`                     | `$all` (no filter)            |
| All events         | `FROM log`                     | `$all` (alias for `all`)      |
| All events         | `FROM $all`                    | `$all` (literal)              |

### Stream Name Resolution

The FROM clause maps user-friendly source names to `$idx-*` stream prefixes:

```
FROM category:order          -> StreamFilter.Prefix("$idx-ce-order")
FROM index:orders-by-country -> StreamFilter.Prefix("$idx-user-orders-by-country")
FROM stream:'order-abc123'   -> TargetStream = "order-abc123"
FROM all                     -> $all, no stream filter
FROM log                     -> $all, no stream filter
FROM $all                    -> $all, no stream filter
```

### Quoting Rules

Source names containing only `[a-zA-Z0-9_.-]` can be unquoted. Names with spaces
or special characters require single quotes:

```sql
FROM category:order                    -- unquoted, simple name
FROM stream:'order-abc123'             -- quoted, hyphens
FROM stream:'my.special/stream'        -- quoted, special chars required
FROM index:orders-by-country           -- unquoted, hyphens allowed
```

### $idx Streams as Predicate Values

The `$idx-*` stream names can also appear directly in predicates, not just in FROM:

```sql
-- Filter by index stream name directly
stream = '$idx-user-orders-by-country'
stream LIKE '$idx-ce-%'
stream = '$idx-et-OrderPlaced'
```

This is valid because `stream` is a STRING column and `$idx-*` values are valid
stream names. The FROM clause is syntactic sugar; direct `$idx-*` references are
the underlying mechanism.

### Examples

```sql
-- No FROM (pure predicate, backwards compatible with v1)
schema = 'OrderPlaced' AND properties.region = 'us-east'

-- Explicit source with WHERE keyword
FROM category:order WHERE schema = 'OrderPlaced'

-- WHERE keyword is optional after FROM
FROM category:order schema = 'OrderPlaced'

-- All events
FROM all WHERE schema = 'OrderPlaced' AND timestamp >= '2026-01-01'

-- Same thing using LOG alias
FROM log WHERE schema = 'OrderPlaced'

-- Same thing using $all
FROM $all WHERE schema = 'OrderPlaced'

-- Specific stream
FROM stream:'order-abc123' WHERE revision > 10
```

---

## User-Defined Index Fields

When querying a user-defined index via `FROM index:{name}`, additional columns
are available based on the index definition.

### Field Access

Index field names are used directly as column names. The `field_` prefix from
the WebUI SQL convention is accepted but not required:

```sql
-- Preferred: direct field name
FROM index:orders-by-country WHERE country = 'Mauritius'

-- Also accepted: field_ prefix (WebUI backwards compat)
FROM index:orders-by-country WHERE field_country = 'Mauritius'
```

Both resolve identically. When `field_` prefix is present, it is stripped and the
remainder is used as the field name.

### Resolution to $idx Streams

Index field filters map to `$idx-user-{name}:{value}` stream prefixes:

```
FROM index:orders-by-country WHERE country = 'Mauritius'
  -> StreamFilter.Prefix("$idx-user-orders-by-country:Mauritius")

FROM index:orders-by-country WHERE field_country = 'Mauritius'
  -> StreamFilter.Prefix("$idx-user-orders-by-country:Mauritius")
```

### Field Resolution Order

When a column name is not in the standard column alias table:

1. If `FROM index:{name}` is active -> treat as index field name
2. Otherwise -> resolve to NULL (unknown column)

---

## Property Access

### Built-in Columns

Direct access using any recognized column name or alias:

```sql
stream = 'orders'
schema = 'OrderPlaced'
event_type = 'OrderPlaced'      -- alias, same as above
timestamp >= '2024-01-01'
created_at >= '2024-01-01'      -- alias, same as above
position > 1000
log_position > 1000             -- alias, same as above
category = 'order'
```

### Custom Properties (Metadata)

Three equivalent syntaxes for accessing metadata key-value pairs:

**Bracket notation** (preferred for keys with special characters):
```sql
properties['correlationId'] = 'abc-123'
properties['order.total'] > 100
```

**Dot notation** (convenient for simple keys):
```sql
properties.userId = 'user-123'
props.amount > 100
metadata.region = 'us-east'     -- metadata is alias for properties
```

**Arrow notation** (DuckDB / PostgreSQL compatible):
```sql
properties->>'correlationId' = 'abc-123'
metadata->>'userId' = 'user-123'
```

---

## JSON Data Access

The `data` column provides access to the record payload when stored as JSON.
Records with non-JSON payloads (protobuf, avro, bytes) evaluate `data` as NULL.

### DuckDB JSON Operators

| Operator | Returns | Description                                   |
|----------|---------|-----------------------------------------------|
| `->`     | JSON    | Extract JSON element at path (preserves type) |
| `->>`    | VARCHAR | Extract element as text string                |

### Path Syntax

Two path formats are supported, following DuckDB conventions:

**Simple key** (string literal):
```sql
data->>'country'                   -- top-level field as text
data->'address'                    -- nested object as JSON
data->'address'->>'city'           -- nested field as text
```

**JSONPath** (dollar notation):
```sql
data->>'$.country'                 -- same as data->>'country'
data->>'$.address.city'            -- nested access in one expression
data->'$.species[0]'               -- array element (0-based)
data->>'$.items[*].name'           -- all array elements
```

### Operator Precedence Note

The `->` operator has low precedence in DuckDB (shared with lambda functions).
Wrap extractions in parentheses when used in comparisons:

```sql
-- Correct: parenthesized
(data->>'amount')::DOUBLE > 100

-- Also correct: ->> returns VARCHAR, auto-coerced by filter engine
data->>'amount' > 100
```

The client filter engine performs automatic type coercion (string-to-number,
string-to-datetime), so explicit `::TYPE` casting is typically unnecessary in
filter expressions. It is documented here for DuckDB SQL compatibility.

### Examples

```sql
-- Simple field access
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->>'country' = 'Mauritius'

-- Numeric comparison (auto-coerced from string)
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->>'amount' > 100

-- Nested JSON object
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->'shipping'->>'method' = 'express'

-- JSONPath notation
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->>'$.address.city' = 'Port Louis'

-- Array element access
FROM category:order
WHERE data->'$.items[0]'->>'sku' = 'WIDGET-001'

-- Null check on JSON field
FROM category:order
WHERE data->>'cancelReason' IS NOT NULL

-- Combining metadata and data filters
FROM category:order
WHERE schema = 'OrderPlaced'
  AND properties.region = 'eu-west'
  AND data->>'amount' > 500
  AND data->>'currency' = 'EUR'

-- Metadata arrow notation alongside data arrow notation
FROM category:order
WHERE metadata->>'correlationId' IS NOT NULL
  AND data->>'status' = 'confirmed'
```

### Dot Notation for Data (Alternative)

For simple top-level field access, dot notation is also supported:

```sql
data.country = 'Mauritius'          -- equivalent to data->>'country'
data.amount > 100                   -- equivalent to data->>'amount'
```

Dot notation always returns the value as a string (like `->>`). For nested access
or array indexing, use arrow notation.

---

## Operators

### Comparison Operators

| Operator | Description      | Example                               |
|----------|------------------|---------------------------------------|
| `=`      | Equality         | `schema = 'OrderPlaced'`              |
| `!=`     | Not equal        | `schema != 'SystemEvent'`             |
| `<>`     | Not equal (alt)  | `stream <> 'test'`                    |
| `>`      | Greater than     | `data->>'amount' > 100`               |
| `<`      | Less than        | `revision < 50`                       |
| `>=`     | Greater or equal | `timestamp >= '2026-01-01T00:00:00Z'` |
| `<=`     | Less or equal    | `position <= 999999`                  |

### Logical Operators

| Operator | Description | Example                                             |
|----------|-------------|-----------------------------------------------------|
| `AND`    | Logical AND | `schema = 'OrderPlaced' AND category = 'order'`     |
| `OR`     | Logical OR  | `schema = 'OrderPlaced' OR schema = 'OrderUpdated'` |
| `NOT`    | Logical NOT | `NOT (category = 'test')`                           |
| `()`     | Grouping    | `(schema = 'A' OR schema = 'B') AND position > 100` |

### Pattern Matching

| Operator    | Description              | Example                     |
|-------------|--------------------------|-----------------------------|
| `LIKE`      | Case-sensitive pattern   | `stream LIKE 'order-%'`     |
| `NOT LIKE`  | Negated pattern          | `stream NOT LIKE '%test%'`  |
| `ILIKE`     | Case-insensitive pattern | `schema ILIKE '%event%'`    |
| `NOT ILIKE` | Negated insensitive      | `stream NOT ILIKE '%TEST%'` |

Wildcards: `%` (any sequence), `_` (single character), `\%` / `\_` (literals).

### Null Checking

| Operator      | Example                            |
|---------------|------------------------------------|
| `IS NULL`     | `properties.correlationId IS NULL` |
| `IS NOT NULL` | `data->>'email' IS NOT NULL`       |

### Set Membership

| Operator | Example                                                       |
|----------|---------------------------------------------------------------|
| `IN`     | `schema IN ('OrderPlaced', 'OrderShipped', 'OrderDelivered')` |
| `NOT IN` | `category NOT IN ('test', 'debug')`                           |

### Range

| Operator      | Example                                           |
|---------------|---------------------------------------------------|
| `BETWEEN`     | `timestamp BETWEEN '2026-01-01' AND '2026-06-30'` |
| `NOT BETWEEN` | `position NOT BETWEEN 100 AND 200`                |

### Operator Precedence

From lowest to highest:

| Level | Operators                             |
|-------|---------------------------------------|
| 1     | `OR`                                  |
| 2     | `AND`                                 |
| 3     | `NOT`                                 |
| 4     | `IS [NOT] NULL`                       |
| 5     | `BETWEEN`, `IN`, `LIKE`, `ILIKE`      |
| 6     | `=`, `!=`, `<>`, `<`, `>`, `<=`, `>=` |
| 7     | `->`, `->>` (JSON extraction)         |

---

## Data Types

### String Literals

Single-quoted. Escape with backslash: `'O\'Reilly'`.

### Numeric Literals

Integers (`42`, `-10`) and decimals (`99.99`, `-12.5`).

### DateTime Literals

ISO 8601 in single quotes:
- `'2026-01-15T10:30:00Z'`
- `'2026-01-15T10:30:00+02:00'`
- `'2026-01-15'` (date only)

### Boolean Literals

`TRUE`, `FALSE` (case-insensitive). Also accepts `'true'`, `'false'` as strings.

### NULL

`NULL` keyword. Use `IS NULL` / `IS NOT NULL` for comparison.

---

## Type Coercion

Automatic coercion following SQL semantics:

- **String comparisons**: case-insensitive by default
- **Numeric promotion**: `decimal` > `double` > `float` > `long` > `int`
- **DateTime**: auto-conversion between `DateTime` and `DateTimeOffset`; strings
  parsed as ISO 8601
- **JSON `->>`**: returns VARCHAR, auto-coerced to numeric/datetime when compared
  with typed literals
- **NULL**: `NULL = NULL` is `false`; `NULL IS NULL` is `true`; any comparison with
  NULL returns `false`

---

## Complete Examples

### Basic Filtering

```sql
-- Simple equality (no FROM, backwards compatible)
stream = 'user-events'

-- Using canonical names
schema = 'OrderPlaced'

-- Using WebUI aliases (identical result)
event_type = 'OrderPlaced'

-- Multiple conditions
schema = 'OrderPlaced' AND properties.amount > 100

-- Category filter
category = 'order' AND schema = 'OrderPlaced'
```

### FROM Clause Queries

```sql
-- Category scope
FROM category:order
WHERE schema = 'OrderPlaced'

-- Specific stream
FROM stream:'order-abc123'
WHERE revision > 5

-- All events with time range
FROM all
WHERE timestamp BETWEEN '2026-01-01' AND '2026-02-01'

-- Using LOG alias
FROM log
WHERE schema = 'OrderPlaced'
  AND category = 'order'

-- Using $all directly
FROM $all
WHERE category IN ('order', 'payment', 'refund')
```

### User-Defined Index Queries

```sql
-- Simple index field filter
FROM index:orders-by-country
WHERE country = 'Mauritius'

-- Index field with additional predicate
FROM index:orders-by-country
WHERE country = 'Mauritius'
  AND timestamp >= '2026-01-01'

-- WebUI field_ prefix (backwards compatible)
FROM index:orders-by-country
WHERE field_country = 'Mauritius'
```

### JSON Data Access

```sql
-- Top-level field
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->>'country' = 'Mauritius'

-- Numeric JSON field
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->>'amount' > 500

-- Nested JSON
FROM category:order
WHERE data->'shipping'->>'method' = 'express'
  AND data->'shipping'->>'estimatedDays' <= 3

-- JSONPath
FROM category:order
WHERE data->>'$.address.city' = 'Port Louis'

-- Array access
FROM category:order
WHERE data->'$.items[0]'->>'sku' = 'WIDGET-001'

-- Null check on data field
FROM category:order
WHERE schema = 'OrderCancelled'
  AND data->>'cancelReason' IS NOT NULL

-- Dot notation (simple fields only)
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data.country = 'Mauritius'
```

### Metadata Access (All Equivalent Syntaxes)

```sql
-- Dot notation
properties.correlationId = 'abc-123'

-- Bracket notation
properties['correlationId'] = 'abc-123'

-- Arrow notation
properties->>'correlationId' = 'abc-123'

-- Using metadata alias with arrow notation
metadata->>'correlationId' = 'abc-123'

-- Using props alias with dot notation
props.correlationId = 'abc-123'
```

### Pattern Matching

```sql
-- Stream pattern within category
FROM category:order
WHERE stream LIKE 'order-premium-%'

-- Case-insensitive schema matching
FROM all WHERE schema ILIKE '%cancelled%'

-- Set membership
FROM category:order
WHERE schema IN ('OrderPlaced', 'OrderShipped', 'OrderDelivered')

-- NOT patterns
FROM log
WHERE category NOT IN ('test', 'debug', 'internal')
  AND schema NOT LIKE '$%'
```

### $idx Stream References in Predicates

```sql
-- Direct index stream reference
stream = '$idx-user-orders-by-country'
stream LIKE '$idx-ce-%'
stream = '$idx-et-OrderPlaced'

-- Combine with other filters
stream LIKE '$idx-user-%' AND timestamp >= '2026-01-01'
```

### Complex Business Logic

```sql
FROM category:order
WHERE schema IN ('OrderPlaced', 'OrderShipped', 'OrderDelivered')
  AND properties.customerType != 'internal'
  AND metadata->>'region' = 'eu-west'
  AND data->>'amount' > 500
  AND data->>'currency' = 'EUR'
  AND timestamp BETWEEN '2026-01-01' AND '2026-06-30'
  AND properties.correlationId IS NOT NULL

FROM index:orders-by-country
WHERE country IN ('France', 'Germany', 'Spain')
  AND schema = 'OrderPlaced'
  AND data->>'amount' > 1000
  AND timestamp >= '2026-01-01'
```

---

## Virtual Table Name Mapping

Complete mapping between FROM source syntax and underlying stream addressing:

| FROM Source                             | Internal Stream Resolution                      | Notes                            |
|-----------------------------------------|-------------------------------------------------|----------------------------------|
| `FROM stream:'{name}'`                  | Direct read of stream `{name}`                  | Quotes optional for simple names |
| `FROM category:{name}`                  | `StreamFilter.Prefix("$idx-ce-{name}")`         | Built-in secondary index         |
| `FROM index:{name}`                     | `StreamFilter.Prefix("$idx-user-{name}")`       | User-defined index               |
| `FROM index:{name}` + field `= '{val}'` | `StreamFilter.Prefix("$idx-user-{name}:{val}")` | Index field filter               |
| `FROM all`                              | `$all`, no stream filter                        | All events in the log            |
| `FROM log`                              | `$all`, no stream filter                        | Alias for `all`                  |
| `FROM $all`                             | `$all`, no stream filter                        | Literal `$all` reference         |
| *(no FROM)*                             | Caller-determined context                       | Pure predicate mode              |

### $idx Stream Name Patterns

| Pattern                         | Description                      | Example                                 |
|---------------------------------|----------------------------------|-----------------------------------------|
| `$idx-ce-{category}`            | Category secondary index         | `$idx-ce-order`                         |
| `$idx-et-{eventType}`           | Event type secondary index       | `$idx-et-OrderPlaced`                   |
| `$idx-user-{indexName}`         | User-defined index (all records) | `$idx-user-orders-by-country`           |
| `$idx-user-{indexName}:{value}` | User-defined index (field value) | `$idx-user-orders-by-country:Mauritius` |

---

## DuckDB Compatibility

### Validation

Expressions are validated against DuckDB to ensure syntax compatibility. The test
schema includes all canonical and alias columns:

```sql
CREATE TABLE records (
    id UUID,
    stream VARCHAR,
    category VARCHAR,
    schema VARCHAR,
    revision BIGINT,
    position BIGINT,
    timestamp TIMESTAMPTZ,
    properties MAP(VARCHAR, VARCHAR),
    data JSON,
    format VARCHAR,
    sequence UBIGINT,
    redacted BOOLEAN
);
```

Alias columns verified via generated columns or a VIEW layer.

### JSON Functions

The filter expression JSON operators map directly to DuckDB functions:

| Expression Syntax | DuckDB Function                       | Returns |
|-------------------|---------------------------------------|---------|
| `data->'key'`     | `json_extract(data, 'key')`           | JSON    |
| `data->>'key'`    | `json_extract_string(data, 'key')`    | VARCHAR |
| `data->'$.path'`  | `json_extract(data, '$.path')`        | JSON    |
| `data->>'$.path'` | `json_extract_string(data, '$.path')` | VARCHAR |

DuckDB JSON uses **0-based indexing** for arrays.

---

## Error Handling

Parse errors follow DuckDB format:

```
Error parsing filter: syntax error at or near "TOKEN"
```

| Error            | Example            | Message                                  |
|------------------|--------------------|------------------------------------------|
| Unexpected token | `schema = AND`     | `syntax error at or near "AND"`          |
| Unclosed string  | `schema = 'Orders` | `syntax error at or near "'"`            |
| Missing operand  | `schema =`         | `syntax error at or near "end of input"` |
| Invalid operator | `schema == 'test'` | `syntax error at or near "="`            |
| Unknown column   | `invalid_col = 1`  | Evaluates to NULL, no parse error        |
| Invalid source   | `FROM unknown:foo` | `syntax error at or near "unknown"`      |

---

## Performance Considerations

1. **Server-pushable columns**: `stream`, `category`, `schema`, `revision`,
   `position`, `timestamp` can be pushed to server-side secondary index evaluation
2. **Metadata filtering**: `properties` / `metadata` requires client-side evaluation
3. **Data filtering**: `data` access requires full event read from the transaction
   log - combine with indexed column filters to minimize IO
4. **Pattern matching**: `LIKE`/`ILIKE` patterns compiled to regex once, cached
5. **Short-circuit evaluation**: `AND`/`OR` use short-circuit semantics
6. **FROM clause**: Resolved at parse time to `$idx-*` stream filters, zero
   runtime overhead vs manual `StreamFilter.Prefix()` calls

---

## Changes from v1.2.0

| Change                 | Type   | v1.2.0                        | v2.0.0                            |
|------------------------|--------|-------------------------------|-----------------------------------|
| FROM clause            | NEW    | Not supported                 | Optional FROM with source scoping |
| `category` column      | NEW    | Not available                 | Derived from stream               |
| `data` column          | NEW    | Planned                       | Implemented with `->` / `->>`     |
| `format` column        | NEW    | Planned as `schema_format`    | Implemented                       |
| `sequence` column      | NEW    | Planned as `sequence_id`      | Implemented                       |
| `redacted` column      | NEW    | Planned                       | Implemented                       |
| `event_type` alias     | NEW    | Not recognized                | Alias for `schema`                |
| `event_number` alias   | NEW    | Not recognized                | Alias for `revision`              |
| `log_position` alias   | NEW    | Not recognized                | Alias for `position`              |
| `created_at` alias     | NEW    | Not recognized                | Alias for `timestamp`             |
| `metadata` alias       | NEW    | Not recognized                | Alias for `properties`            |
| `->` operator          | NEW    | Not supported                 | JSON extraction (returns JSON)    |
| `->>` operator         | NEW    | Not supported                 | JSON extraction (returns VARCHAR) |
| JSONPath syntax        | NEW    | Not supported                 | `'$.path'` in JSON operators      |
| `LOG` source           | NEW    | Not supported                 | Alias for `ALL` in FROM           |
| `$all` source          | NEW    | Not supported                 | Literal in FROM                   |
| `$idx-*` in predicates | NEW    | Not documented                | Explicit support                  |
| `id` canonical         | RENAME | `record_id` was primary       | `id` is canonical                 |
| `schema` canonical     | RENAME | `schema_name` was primary     | `schema` is canonical             |
| `revision` canonical   | RENAME | `stream_revision` was primary | `revision` is canonical           |
| `sequence` canonical   | RENAME | `sequence_id` was planned     | `sequence` is canonical           |
| `field_` prefix        | COMPAT | N/A                           | Accepted, stripped, not canonical |

---

## References

- [DuckDB JSON Functions](https://duckdb.org/docs/extensions/json/overview)
- [DuckDB Comparison Operators](https://duckdb.org/docs/sql/expressions/comparison_operators)
- [DuckDB Pattern Matching](https://duckdb.org/docs/sql/functions/pattern_matching)
- [DuckDB IN Operator](https://duckdb.org/docs/sql/expressions/in)
- [KurrentDB Queries UI](https://docs.kurrent.io/server/v26.0/features/queries/ui.html)
- [KurrentDB User-Defined Indexes](https://docs.kurrent.io/server/v26.0/features/indexes/user-defined.html)
- [KurrentDB Secondary Indexes](https://docs.kurrent.io/server/v25.1/features/indexes/secondary.html)
