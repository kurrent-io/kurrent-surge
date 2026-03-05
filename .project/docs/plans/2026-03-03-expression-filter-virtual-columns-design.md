# Expression Filter Virtual Columns and Index Stream Resolution

## Contents

- [Context](#context)
- [Scope](#scope)
- [Non-scope](#non-scope)
- [Virtual column mapping](#virtual-column-mapping)
- [Server filter extraction](#server-filter-extraction)
- [RecordFilter factory changes](#recordfilter-factory-changes)
- [StreamFilter.Prefix removal](#streamfilterprefix-removal)
- [PropertyAccess alias expansion](#propertyaccess-alias-expansion)
- [Category derivation](#category-derivation)
- [Index client-side semantics](#index-client-side-semantics)
- [Expression examples](#expression-examples)
- [Conflict resolution](#conflict-resolution)

## Context

The KurrentDB server exposes secondary index streams as first-class streams using `$idx-*` naming conventions.
Category indexes live at `$idx-ce-{category}`, event type indexes at `$idx-et-{eventType}`, and user-defined
indexes at `$idx-user-{indexName}`. These streams are readable through the existing V1 gRPC read API using
standard stream name targeting.

The current `ExpressionFilter` parser supports filtering by `stream`, `schema`, `timestamp`, `position`,
`revision`, `id`, and `properties`. It has no concept of `category`, `index`, or the `$idx-*` stream
resolution. The `ServerFilterExtractor` lifts `stream` predicates to `StreamFilter` and `schema` predicates
to `SchemaFilter`, but cannot route to secondary index streams.

Additionally, `StreamFilter.Prefix` exists as a workaround for category filtering via stream name prefix
matching (e.g., `orders-*`). This model is superseded by direct `$idx-ce-*` stream reads.

## Scope

- Add `category` and `index` as virtual columns in the expression filter parser
- Resolve `category` and `index` predicates to `$idx-*` stream names in `ServerFilterExtractor`
- Expand `PropertyAccess.Resolve` with full spec v2 alias parity
- Remove `StreamFilter.Prefix` and `RecordFilter.ByStreamPrefix`
- Update `RecordFilter.ByCategory` to route through `$idx-ce-*` streams

## Non-scope

- FROM clause syntax (deferred to a later iteration)
- User-defined index field resolution (requires FROM clause context)
- JSON data access via `->` and `->>` operators
- `SchemaFilter` migration to `$idx-et-*` (kept on `EventType.Regex` V1 gRPC path for now)

## Virtual column mapping

All virtual columns resolve to `StreamFilter` with a direct stream name. No prefix matching.

| Expression                    | Server resolution                             | TargetStream                  | Filter type  |
|-------------------------------|-----------------------------------------------|-------------------------------|--------------|
| `stream = 'orders-99'`        | `StreamFilter("orders-99")`                   | `orders-99`                   | StreamFilter |
| `category = 'orders'`         | `StreamFilter("$idx-ce-orders")`              | `$idx-ce-orders`              | StreamFilter |
| `index = 'orders-by-country'` | `StreamFilter("$idx-user-orders-by-country")` | `$idx-user-orders-by-country` | StreamFilter |
| `schema = 'OrderPlaced'`      | `SchemaFilter("^OrderPlaced$")`               | n/a (EventType.Regex V1 gRPC) | SchemaFilter |
| `event_type = 'OrderPlaced'`  | `SchemaFilter("^OrderPlaced$")`               | n/a (alias for schema)        | SchemaFilter |

## Server filter extraction

The `ServerFilterExtractor` priority order changes from two levels to four:

1. `stream` predicates (=, LIKE, IN) produce `StreamFilter` (unchanged)
2. `category` predicates (=, LIKE, IN) produce `StreamFilter("$idx-ce-{value}")`
3. `index` predicates (= only) produce `StreamFilter("$idx-user-{value}")`
4. `schema` predicates (=, LIKE, IN) produce `SchemaFilter` (unchanged)
5. Fallback: `RecordFilter.None`

Category LIKE example: `category LIKE 'order%'` produces a regex on the `$idx-ce-` prefix space.

Index supports equality only. Pattern matching on index names is not meaningful.

## RecordFilter factory changes

```csharp
// ByCategory changes from stream prefix to $idx-ce-* stream targeting
RecordFilter.ByCategory("orders")
// Before: StreamFilter.Prefix("orders-")
// After:  StreamFilter("$idx-ce-orders")  →  equivalent to RecordFilter.ByStream("$idx-ce-orders")

// ByStreamPrefix removed
RecordFilter.ByStreamPrefix("orders-")  // REMOVED

// ByStream unchanged
RecordFilter.ByStream("orders-99")
RecordFilter.ByStream("$idx-ce-orders")           // works directly
RecordFilter.ByStream("$idx-user-orders-by-country")  // works directly

// ByExpression gains virtual column support
RecordFilter.ByExpression("category = 'orders'")
RecordFilter.ByExpression("index = 'orders-by-country'")
```

## StreamFilter.Prefix removal

`StreamFilter` currently supports three construction modes: exact match, prefix match (non-anchored regex),
and raw regex. The prefix mode is removed.

- Remove the `exactMatch` parameter from `StreamFilter` constructors
- Remove `RecordFilter.ByStreamPrefix`
- Remove `RecordFilter.BySchemaPrefix` (uses similar prefix logic on `SchemaFilter`)
- Callers that need prefix behavior can use `RecordFilter.ByStreamPattern("^orders-")` directly

## PropertyAccess alias expansion

Full spec v2 alias table added to `PropertyAccess.Resolve`:

| Canonical    | New aliases                                                   | Maps to                             |
|--------------|---------------------------------------------------------------|-------------------------------------|
| `stream`     | `stream_name`, `rec.stream`, `rec.position.stream`            | `record.Stream.Value`               |
| `category`   | `rec.category`                                                | derived from `record.Stream.Value`  |
| `schema`     | `event_type`, `rec.schema.name`                               | `record.Schema.SchemaName.Value`    |
| `revision`   | `event_number`, `rec.revision`, `rec.position.streamRevision` | `(long)record.Revision`             |
| `position`   | `log_position`, `rec.position.logPosition`                    | `record.Position.LogPosition.Value` |
| `timestamp`  | `created_at`, `created_ts`, `rec.timestamp`                   | `record.Timestamp`                  |
| `properties` | `metadata`, `rec.properties`                                  | `record.Metadata[Key]`              |
| `id`         | `rec.id`                                                      | `record.Id`                         |
| `index`      | n/a                                                           | always `true` (routing-only)        |

The `field_` prefix is stripped as a fallback: if a column name starts with `field_`, the prefix is removed
and the name is re-resolved through the alias table.

Multi-segment dot notation (`rec.schema.name`, `rec.position.stream`) is handled by chaining identifier
tokens through the parser's `ParsePropertyAccess` method into a single `PropertyAccess` with the full
dotted name.

## Category derivation

Client-side, `category` is derived from the stream name by extracting text before the first `-`:

- `stream = 'order-abc123'` results in `category = 'order'`
- `stream = 'payment-2026-001'` results in `category = 'payment'`
- `stream = 'nodelimiter'` results in `category = 'nodelimiter'`

## Index client-side semantics

The `index` virtual column has no corresponding field on a `Record`. Client-side evaluation always returns
`true`. The server already filtered records by index membership when reading from the `$idx-user-*` stream.

## Expression examples

```sql
-- Category filter: reads from $idx-ce-orders stream
category = 'orders'

-- Category with additional predicate: reads from $idx-ce-orders, schema checked client-side
category = 'orders' AND schema = 'OrderPlaced'

-- Index filter: reads from $idx-user-orders-by-country stream
index = 'orders-by-country'

-- Index with metadata predicate: reads from index stream, properties checked client-side
index = 'orders-by-country' AND properties.region = 'eu-west'

-- Using WebUI aliases: event_type is alias for schema
event_type = 'OrderPlaced' AND created_at >= '2026-01-01'

-- Using rec.* dot notation
rec.schema.name = 'OrderPlaced' AND rec.position.logPosition > 1000

-- Direct $idx-* stream reference (works today, unchanged)
stream = '$idx-ce-orders'
```

## Conflict resolution

When multiple virtual columns appear in AND, the highest-priority extraction wins:

- `stream = 'X' AND category = 'Y'` results in stream winning (priority 1 > 2); category evaluated client-side
- `category = 'X' AND index = 'Y'` results in category winning (priority 2 > 3); index always true client-side
- `category = 'X' AND schema = 'Y'` results in category winning (priority 2 > 4); schema evaluated client-side
- `index = 'X' AND schema = 'Y'` results in index winning (priority 3 > 4); schema evaluated client-side
