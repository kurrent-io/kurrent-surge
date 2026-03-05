# Filter Expression Syntax Alignment - Investigation Findings

**Date:** 2026-02-17
**Status:** Research Complete - Awaiting Decision
**Scope:** Alignment between client-side filter expressions spec, KurrentDB WebUI SQL
query engine, and user-defined index filter/selector syntax

---

## 1. Context

The current [Filter Expressions Specification](../docs/features/filter-expressions-spec.md)
(v1.2.0, 2025-11-19) defines a SQL-like WHERE clause language for client-side filtering of
`Record` instances. This language is DuckDB-compatible by design.

KurrentDB v25.1 introduced a **WebUI SQL query engine** (DuckDB-powered) and
**secondary indexes**. KurrentDB v26.0 introduced **user-defined indexes** with
JavaScript filter/selector expressions. Each system uses its own column naming
conventions and access syntax, creating friction for users who move between contexts.

This document catalogues every discrepancy found.

---

## 2. The Three Systems

### 2.1 Client Filter Expressions (this repo)

- **Language:** SQL-like WHERE clause subset (DuckDB-compatible)
- **Where used:** `RecordFilter.ByExpression(...)`, client-side in-memory evaluation
- **Spec:** `.project/docs/features/filter-expressions-spec.md`
- **Implementation:** `ExpressionFilter.Parser.cs` (Pratt parser + tokenizer)
- **Example:** `stream = 'orders' AND schema_name = 'OrderPlaced' AND properties.amount > 100`

### 2.2 WebUI SQL Query Engine (KurrentDB server)

- **Language:** Full DuckDB SQL
- **Where used:** Browser query page at `/ui/query` (requires license, v25.1+)
- **Documentation:** https://docs.kurrent.io/server/v26.0/features/queries/ui.html
- **Example:** `SELECT * FROM category:order WHERE event_type = 'OrderPlaced'`

### 2.3 User-Defined Index Definitions (KurrentDB server)

- **Language:** JavaScript arrow functions receiving a `rec` object
- **Where used:** `POST /v2/indexes/{name}` HTTP API (v26.0+)
- **Documentation:** https://docs.kurrent.io/server/v26.0/features/indexes/user-defined.html
- **Example filter:** `rec => rec.schema.name == 'OrderCreated'`
- **Example selector:** `rec => rec.value.country`

---

## 3. The Server-Side Record Model (`rec` object)

The `rec` object exposed to user-defined index JavaScript expressions is the
canonical server-side representation of a record/event:

```json
{
  "id": "12345678-1234-1234-1234-123456789abc",
  "timestamp": "2026-01-15T13:37:01.337Z",
  "position": {
    "stream": "my-stream",
    "streamRevision": 2,
    "logPosition": 2705
  },
  "schema": {
    "name": "my-event-type",
    "format": "Json"
  },
  "sequence": 3,
  "redacted": false,
  "value": { "my": "data" },
  "properties": { "my": "metadata" }
}
```

### Field Reference

| Field | Type | Description |
|---|---|---|
| `id` | UUID string | Event unique identifier |
| `timestamp` | ISO-8601 string | Time the event was written to the transaction log |
| `position.stream` | string | Stream name |
| `position.streamRevision` | integer | Event number within the stream |
| `position.logPosition` | integer | Commit position in the transaction log |
| `schema.name` | string | Event type name |
| `schema.format` | string | Data serialization format (e.g., `"Json"`) |
| `sequence` | integer | Auto-incrementing counter per record |
| `redacted` | boolean | Whether the record has been redacted |
| `value` | object | Deserialized event body (only present for JSON, non-redacted) |
| `properties` | object | Deserialized event metadata |

---

## 4. Column/Field Name Alignment Matrix

### 4.1 Implemented Columns

| Concept | Client Spec Column | Client Aliases | WebUI SQL Column | Index `rec` Path | Aligned? |
|---|---|---|---|---|---|
| Event ID | `record_id` | `id` | *(not exposed)* | `rec.id` | PARTIAL - `record_id` primary vs `id` in rec |
| Stream name | `stream` | - | `stream` | `rec.position.stream` | YES - all use `stream` |
| Category | *(missing)* | - | `category` | *(derived from stream)* | GAP - absent from client spec |
| Event type | `schema_name` | `schema` | `event_type` | `rec.schema.name` | NO - three different names |
| Stream position | `stream_revision` | `revision` | `event_number` | `rec.position.streamRevision` | NO - three different names |
| Log position | `position` | `pos` | `log_position` | `rec.position.logPosition` | NO - three different names |
| Timestamp | `timestamp` | `ts` | `created_at` | `rec.timestamp` | PARTIAL - client/rec match, WebUI differs |
| Metadata | `properties` | `props` | `metadata` | `rec.properties` | PARTIAL - client/rec match, WebUI differs |

### 4.2 Planned Columns (Not Yet Implemented in Client)

| Concept | Client Spec Column | Client Aliases | WebUI SQL Column | Index `rec` Path | Notes |
|---|---|---|---|---|---|
| Event body | `data` | - | `data` | `rec.value` | Client/WebUI match, rec uses `value` |
| Schema format | `schema_format` | `format` | *(not exposed)* | `rec.schema.format` | - |
| Sequence | `sequence_id` | `seq` | *(not exposed)* | `rec.sequence` | Client uses `sequence_id`, rec uses `sequence` |
| Redacted | `redacted` | - | *(not exposed)* | `rec.redacted` | Aligned |
| Schema ID | `schema_id` | - | *(not exposed)* | *(not in rec)* | Client-only concept |

---

## 5. Detailed Discrepancy Analysis

### 5.1 Event Type: `schema_name` vs `event_type` vs `rec.schema.name`

**Severity: HIGH** - This is the most user-visible column name.

- **Client spec:** `schema_name` (primary), `schema` (alias)
- **WebUI SQL:** `event_type`
- **Index `rec`:** `rec.schema.name`
- **Legacy v1 gRPC proto:** `EventType` in `FilterOptions.Expression`

A user writing `event_type = 'OrderPlaced'` in the WebUI must rewrite it as
`schema_name = 'OrderPlaced'` (or `schema = 'OrderPlaced'`) for the client filter API.

The naming reflects a philosophical shift: KurrentDB v2 protocol moved from "events" to
"records" and from "event types" to "schemas". The WebUI retained the legacy "event"
terminology for user familiarity.

### 5.2 Stream Position: `stream_revision` vs `event_number` vs `rec.position.streamRevision`

**Severity: MEDIUM** - Used in positional queries.

- **Client spec:** `stream_revision` (primary), `revision` (alias)
- **WebUI SQL:** `event_number`
- **Index `rec`:** `rec.position.streamRevision`

Client and `rec` object share the root term `streamRevision`, but the WebUI uses
`event_number` (legacy EventStoreDB terminology).

### 5.3 Log Position: `position` vs `log_position` vs `rec.position.logPosition`

**Severity: MEDIUM**

- **Client spec:** `position` (primary), `pos` (alias)
- **WebUI SQL:** `log_position`
- **Index `rec`:** `rec.position.logPosition`

The client spec uses the shortest form `position`. The WebUI and `rec` object both
include the `log` qualifier to distinguish from stream position.

### 5.4 Timestamp: `timestamp` vs `created_at` vs `rec.timestamp`

**Severity: LOW-MEDIUM**

- **Client spec:** `timestamp` (primary), `ts` (alias)
- **WebUI SQL:** `created_at`
- **Index `rec`:** `rec.timestamp`

Client and `rec` object are aligned. WebUI uses `created_at` (common in database schemas).

### 5.5 Metadata: `properties` vs `metadata` vs `rec.properties`

**Severity: MEDIUM** - Affects all metadata filtering expressions.

- **Client spec:** `properties` (primary), `props` (alias) - typed as MAP
- **WebUI SQL:** `metadata` - typed as JSON, accessed via `metadata->>'key'`
- **Index `rec`:** `rec.properties` - typed as object

Client and `rec` object are aligned on `properties`. WebUI uses `metadata` (legacy term).

Access syntax differs:
- Client: `properties['key']` or `properties.key`
- WebUI: `metadata->>'key'`
- Index JS: `rec.properties.key`

### 5.6 Missing `category` Column

**Severity: MEDIUM** - `category` is a first-class indexed column in the WebUI.

The WebUI exposes `category` as an indexed field derived from the stream name (text
before the first `-` separator). The client spec has no equivalent. Users cannot write
`category = 'order'` in client-side filter expressions.

The `rec` object also does not expose `category` directly - it is derived from
`rec.position.stream`.

### 5.7 Event Body: `data` (planned) vs `data` vs `rec.value`

**Severity: LOW** (planned feature)

- **Client spec:** `data` (planned, not yet implemented)
- **WebUI SQL:** `data` - accessed via `data->>'field'`
- **Index `rec`:** `rec.value`

Client and WebUI would align on `data`. The `rec` object uses `value` instead.

Planned access syntax in client spec:
- Dot notation: `data.userId = 'user-123'`
- Arrow notation: `data->>'userId' = 'user-123'`

WebUI uses only arrow notation: `data->>'field'`

---

## 6. Data Access Syntax Comparison

### 6.1 Metadata Access

| System | Syntax | Example |
|---|---|---|
| Client filter | Bracket: `properties['key']` | `properties['userId'] = 'user-123'` |
| Client filter | Dot: `properties.key` | `properties.userId = 'user-123'` |
| WebUI SQL | Arrow: `metadata->>'key'` | `metadata->>'userId' = 'user-123'` |
| Index JS | JS dot: `rec.properties.key` | `rec.properties.userId` |

### 6.2 Event Body Access

| System | Syntax | Example | Status |
|---|---|---|---|
| Client filter | Dot: `data.field` | `data.country = 'US'` | Planned |
| Client filter | Arrow: `data->>'field'` | `data->>'country' = 'US'` | Planned |
| WebUI SQL | Arrow: `data->>'field'` | `data->>'country' = 'US'` | Implemented |
| WebUI SQL | Nested: `data->'obj'->>'field'` | `data->'address'->>'city'` | Implemented |
| Index JS | JS dot: `rec.value.field` | `rec.value.country` | Implemented |

### 6.3 Type Casting in WebUI SQL

The WebUI supports DuckDB type casting for JSON values:

```sql
WHERE (data->>'amount')::DOUBLE > 100.0
```

The client filter spec has automatic type coercion built in, so explicit casting is
not needed (and not supported).

---

## 7. Secondary Indexes

### 7.1 Built-In Secondary Indexes (v25.1+)

Two default indexes, stored in DuckDB, replacing system projections:

| Index Type | Replaces | Stream Pattern | WebUI Equivalent |
|---|---|---|---|
| Category | `$by_category` | `$idx-ce-{CATEGORYNAME}` | `category:{name}` virtual table |
| Event type | `$by_event_type` | `$idx-et-{EVENTTYPE}` | `event_type` indexed column |

**Access via client API:**

```csharp
// Category index
client.ReadAllAsync(Direction.Forwards, Position.Start,
    StreamFilter.Prefix("$idx-ce-order"));

// Event type index
client.ReadAllAsync(Direction.Forwards, Position.Start,
    StreamFilter.Prefix("$idx-et-OrderPlaced"));
```

**Relationship to filter expressions:** The `ServerFilterExtractor` in the client
converts applicable filter predicates (e.g., `stream = 'x'`, `schema_name = 'y'`,
`stream LIKE 'order%'`) into `StreamFilter`/`SchemaFilter` regex patterns. The server
then maps these to secondary index lookups. This is transparent to the user.

### 7.2 User-Defined Indexes (v26.0+)

Custom indexes based on event content. Created via HTTP API:

```http
POST /v2/indexes/orders-by-country
Content-Type: application/json

{
  "filter": "rec => rec.schema.name == 'OrderCreated'",
  "fields": [
    {
      "name": "country",
      "selector": "rec => rec.value.country",
      "type": "INDEX_FIELD_TYPE_STRING"
    }
  ]
}
```

**Field types:**

| HTTP API Type | Description |
|---|---|
| `INDEX_FIELD_TYPE_STRING` | String values |
| `INDEX_FIELD_TYPE_DOUBLE` | Floating point |
| `INDEX_FIELD_TYPE_INT_32` | 32-bit integer |
| `INDEX_FIELD_TYPE_INT_64` | 64-bit integer |

**Reading user-defined indexes:**

| Goal | Stream Prefix Filter | WebUI SQL |
|---|---|---|
| Entire index | `$idx-user-{name}` | `SELECT * FROM index:{name}` |
| By field value | `$idx-user-{name}:{value}` | `WHERE field_{fieldname} = '{value}'` |

**JavaScript expression constraints:**
- Filter must return boolean; non-boolean -> record excluded + error logged
- Selector must return type-compatible value; incompatible -> excluded + error logged
- Selector can return `'skip'` string literal to exclude without error
- Filter is optional; omitting includes all user records
- Both must be deterministic based on record content
- Currently max 1 field per index

**Error handling:**

| Situation | Outcome |
|---|---|
| Filter returns non-boolean | Record excluded; error logged |
| Selector returns incompatible type | Record excluded; error logged |
| Selector returns `'skip'` | Record excluded; no error |
| Filter not provided | All user records included |

### 7.3 Index Management API

| Operation | Method | Endpoint | Permissions |
|---|---|---|---|
| Create | POST | `/v2/indexes/{name}` | Admin/Operations |
| Start | POST | `/v2/indexes/{name}/start` | Admin/Operations |
| Stop | POST | `/v2/indexes/{name}/stop` | Admin/Operations |
| Delete | DELETE | `/v2/indexes/{name}` | Admin/Operations |
| List all | GET | `/v2/indexes/` | Any authenticated |
| Get one | GET | `/v2/indexes/{name}` | Any authenticated |

### 7.4 Secondary Index Characteristics

- Stored in embedded DuckDB, separate from transaction log
- **Eventually consistent** - brief delay between write and index visibility
- Up to 50% less storage vs system projections
- Up to 10x faster reads vs system projections
- No write amplification (no link events)
- Backup requires volume snapshots (DuckDB files are not append-only)
- Currently requires `$admins` group membership for reads
- Enabled by default; disable via `SecondaryIndexing.Enabled: false`

### 7.5 Index Stream Name Patterns

| Index Type | Pattern | Example |
|---|---|---|
| Category (built-in) | `$idx-ce-{CATEGORYNAME}` | `$idx-ce-order` |
| Event type (built-in) | `$idx-et-{EVENTTYPE}` | `$idx-et-OrderPlaced` |
| User-defined (all) | `$idx-user-{INDEX-NAME}` | `$idx-user-orders-by-country` |
| User-defined (filtered) | `$idx-user-{INDEX-NAME}:{VALUE}` | `$idx-user-orders-by-country:Mauritius` |

---

## 8. Index Addressing and Naming Conventions

How indexes are referenced differs across every access context. This section
consolidates all the naming patterns, prefixes, and addressing syntaxes.

### 8.1 Stream Name Patterns (Internal Representation)

Every index - built-in or user-defined - is addressed internally as a stream name
with the `$idx` prefix. These are the actual stream identifiers used in the gRPC/client
API and stored in the transaction log metadata.

| Index Type | Stream Name Pattern | Example |
|---|---|---|
| Category (built-in) | `$idx-ce-{CATEGORYNAME}` | `$idx-ce-order` |
| Event type (built-in) | `$idx-et-{EVENTTYPE}` | `$idx-et-OrderPlaced` |
| User-defined (entire index) | `$idx-user-{INDEX-NAME}` | `$idx-user-orders-by-country` |
| User-defined (by field value) | `$idx-user-{INDEX-NAME}:{FIELD-VALUE}` | `$idx-user-orders-by-country:Mauritius` |

**Prefix constants:**
- All indexes: `$idx` (defined as `StreamName.IndexStreamsPrefix` in the client)
- Category: `$idx-ce-`
- Event type: `$idx-et-`
- User-defined: `$idx-user-`

The `IndexName` value object in the client enforces the `$idx` prefix requirement.
`StreamName.IsIndex` returns `true` for any stream starting with `$idx`.

### 8.2 Client API Access (gRPC / .NET SDK)

Indexes are accessed via filtered `$all` reads using `StreamFilter.Prefix()`:

```csharp
// Built-in: category index
client.ReadAllAsync(Direction.Forwards, Position.Start,
    StreamFilter.Prefix("$idx-ce-order"));

// Built-in: event type index
client.ReadAllAsync(Direction.Forwards, Position.Start,
    StreamFilter.Prefix("$idx-et-OrderPlaced"));

// User-defined: entire index
client.ReadAllAsync(Direction.Forwards, Position.Start,
    StreamFilter.Prefix("$idx-user-orders-by-country"));

// User-defined: filtered by field value
client.ReadAllAsync(Direction.Forwards, Position.Start,
    StreamFilter.Prefix("$idx-user-orders-by-country:Mauritius"));
```

Rules:
- Always use `StreamFilter.Prefix()` (not regex or exact match)
- Provide exactly **one** index name per filter
- Progress tracked via **log position**, not event number
- No `resolveLinkTos` needed (unlike legacy system projection reads)
- Subscriptions use default page size of **2048** for index reads

### 8.3 WebUI SQL Access (DuckDB Virtual Tables)

The WebUI maps indexes to virtual table syntax and column names, abstracting
away the `$idx-` stream prefixes entirely:

| Concept | Stream Name (Client API) | WebUI SQL Syntax |
|---|---|---|
| Specific stream | Direct stream read | `stream:{stream-name}` |
| Category | `$idx-ce-{name}` | `category:{name}` |
| Event type | `$idx-et-{type}` | Column filter: `WHERE event_type = '{type}'` |
| All events | `$all` with no filter | `all_events` |
| User-defined index | `$idx-user-{name}` | `index:{name}` |
| User-defined (by field) | `$idx-user-{name}:{value}` | `WHERE field_{fieldname} = '{value}'` |

Examples:

```sql
-- Category index (instead of $idx-ce-order)
SELECT * FROM category:order WHERE event_type = 'OrderPlaced';

-- Event type index (instead of $idx-et-OrderPlaced)
SELECT * FROM all_events WHERE event_type = 'OrderPlaced';

-- User-defined index (instead of $idx-user-orders-by-country)
SELECT * FROM index:orders-by-country;

-- User-defined index filtered (instead of $idx-user-orders-by-country:Mauritius)
SELECT * FROM index:orders-by-country WHERE field_country = 'Mauritius';
```

Note: the `index:{name}` virtual table uses the **index name only** (no `$idx-user-`
prefix). The WebUI strips the prefix automatically.

### 8.4 HTTP Management API (Index CRUD)

Index names in the management API also omit the `$idx-user-` prefix:

```http
POST   /v2/indexes/orders-by-country          -- Create
GET    /v2/indexes/orders-by-country          -- Get
DELETE /v2/indexes/orders-by-country          -- Delete
POST   /v2/indexes/orders-by-country/start    -- Start
POST   /v2/indexes/orders-by-country/stop     -- Stop
GET    /v2/indexes/                           -- List all
```

The server adds the `$idx-user-` prefix internally when creating the index stream.

### 8.5 gRPC Management API (Proto)

The proto uses a different endpoint prefix:

```
POST   /v2/custom-indexes/{name}              -- CreateCustomIndex
POST   /v2/custom-indexes/{name}/enable       -- EnableCustomIndex
POST   /v2/custom-indexes/{name}/disable      -- DisableCustomIndex
DELETE /v2/custom-indexes/{name}              -- DeleteCustomIndex
GET    /v2/custom-indexes/{name}              -- GetCustomIndex
GET    /v2/custom-indexes                     -- ListCustomIndexes
```

Note the discrepancy: HTTP API uses `/v2/indexes/`, gRPC uses `/v2/custom-indexes/`.

### 8.6 Legacy System Projection Streams (Replaced by Secondary Indexes)

For reference, the legacy equivalents that secondary indexes replace:

| Legacy Stream | Legacy Read Method | Replacement Index |
|---|---|---|
| `$ce-{category}` | `ReadStreamAsync("$ce-order", resolveLinkTos: true)` | `$idx-ce-{category}` via `ReadAllAsync` |
| `$et-{eventType}` | `ReadStreamAsync("$et-OrderPlaced", resolveLinkTos: true)` | `$idx-et-{eventType}` via `ReadAllAsync` |

### 8.7 Complete Cross-Reference Table

| What You Want | Client API (stream filter) | WebUI SQL | HTTP Mgmt API | gRPC Mgmt API |
|---|---|---|---|---|
| All events | No filter on `$all` | `FROM all_events` | N/A | N/A |
| One stream | `ReadStreamAsync("order-123")` | `FROM stream:order-123` | N/A | N/A |
| Category | `StreamFilter.Prefix("$idx-ce-order")` | `FROM category:order` | N/A | N/A |
| Event type | `StreamFilter.Prefix("$idx-et-OrderPlaced")` | `WHERE event_type = 'OrderPlaced'` | N/A | N/A |
| User index (all) | `StreamFilter.Prefix("$idx-user-by-country")` | `FROM index:by-country` | `GET /v2/indexes/by-country` | `GET /v2/custom-indexes/by-country` |
| User index (value) | `StreamFilter.Prefix("$idx-user-by-country:US")` | `WHERE field_country = 'US'` | N/A | N/A |
| Create index | N/A | N/A | `POST /v2/indexes/{name}` | `CreateCustomIndex` |
| Start/stop | N/A | N/A | `POST .../start`, `POST .../stop` | `Enable`/`Disable` |
| Delete index | N/A | N/A | `DELETE /v2/indexes/{name}` | `DeleteCustomIndex` |

---

## 9. WebUI SQL Virtual Tables and Columns

### 9.1 Virtual Tables

| Virtual Table | Syntax | Description | Version |
|---|---|---|---|
| Stream | `stream:{name}` | Events from a specific stream | v25.1+ |
| Category | `category:{name}` | Events from all streams in a category | v25.1+ |
| All events | `all_events` | Every event in the database | v25.1+ |
| User-defined index | `index:{name}` | Events matching a user-defined index | v26.0+ |

### 9.2 Column Reference

**Indexed fields (fast - predicate pushdown via DuckDB):**

| Column | Type | Description |
|---|---|---|
| `stream` | string | Stream name |
| `category` | string | Stream category (derived: text before first `-`) |
| `event_type` | string | Event type name |
| `event_number` | integer | Position within the stream |
| `log_position` | integer | Global commit position in the transaction log |
| `created_at` | timestamp | Event creation timestamp |

**Data fields (slow - require full event read from log):**

| Column | Type | Description |
|---|---|---|
| `data` | JSON | Event body (deserialized) |
| `metadata` | JSON | Event metadata (deserialized) |

**User-defined index fields (v26.0+):**

| Column Pattern | Example | Description |
|---|---|---|
| `field_{name}` | `field_country` | Extracted field value from index definition |

### 9.3 JSON Access Operators

| Operator | Returns | Description | Example |
|---|---|---|---|
| `->>'key'` | text | Extract JSON field as text | `data->>'orderId'` |
| `->'key'` | JSON | Extract JSON field as JSON (for nesting) | `data->'address'->>'city'` |

### 9.4 SQL Features Supported

Full DuckDB SQL including: `SELECT`, `WHERE`, `DISTINCT`, `COUNT`, `GROUP BY`,
`ORDER BY`, `LIMIT`, `JOIN`, aggregation functions, `DATE_TRUNC`, type casting
(`::DOUBLE`), and standard SQL constructs.

### 9.5 Constraints

- JSON access only works when data/metadata are in JSON format (binary skipped)
- No advanced query planning beyond default secondary indexes
- Sequential event processing for data/metadata reads
- `GROUP BY`/`ORDER BY` require full enumeration even with `LIMIT`
- Large `SELECT *` queries can exhaust browser or server memory

---

## 10. gRPC Proto vs HTTP API Discrepancies

The local gRPC proto (`proto/kurrentdb/protocol/v2/indexes/indexes.proto`) diverges
from the published HTTP API documentation:

| Aspect | HTTP API (docs.kurrent.io) | gRPC Proto (local repo) |
|---|---|---|
| Endpoint prefix | `/v2/indexes/` | `/v2/custom-indexes/` |
| Index fields | `fields[]` array with `name`, `selector`, `type` | Single `partition_key_selector` + `partition_key_type` |
| Field types | 4 types: `INDEX_FIELD_TYPE_{STRING,DOUBLE,INT_32,INT_64}` | 7 types: `KEY_TYPE_{STRING,NUMBER,INT_16,INT_32,INT_64,UNSIGNED_INT_32,UNSIGNED_INT_64}` |
| Lifecycle | `start`/`stop` endpoints | `EnableCustomIndex`/`DisableCustomIndex` RPCs |
| Status enum | Not detailed | `CUSTOM_INDEX_STATUS_{UNSPECIFIED,DISABLED,ENABLED,DELETED}` |
| Multi-field support | `fields` array (max 1 currently) | Single field only (no array) |
| Filter field | `filter` (string) | `filter` (string) |

The proto appears to represent an earlier or internal iteration of the API.
The HTTP API documentation reflects the publicly released v26.0 surface.

---

## 11. Naming Philosophy Analysis

Three naming philosophies coexist:

| Philosophy | System | Origin | Examples |
|---|---|---|---|
| **Record-centric** (v2) | Client filter spec | KurrentDB v2 protocol rebranding | `record_id`, `schema_name`, `stream_revision`, `properties` |
| **Event-centric** (legacy) | WebUI SQL | EventStoreDB historical terminology | `event_type`, `event_number`, `created_at`, `metadata` |
| **Structured/nested** | Index `rec` object | Server-side record model | `rec.schema.name`, `rec.position.streamRevision`, `rec.value` |

The client spec and `rec` object share vocabulary roots (`schema`, `streamRevision`,
`properties`) but differ in structure (flat columns vs nested object). The WebUI SQL
retains legacy EventStoreDB terminology that existing users are familiar with.

---

## 12. Client Filter Expression Parser - Current Column Resolution

From `PropertyAccess.Resolve()` in `ExpressionFilter.Parser.cs`:

```
"stream"                        -> record.Stream.Value
"schema_name" or "schema"       -> record.Schema.SchemaName.Value
"timestamp" or "ts"             -> record.Timestamp
"position" or "pos"             -> record.Position.LogPosition.Value
"stream_revision" or "revision" -> (long)record.StreamRevision
"record_id" or "id"             -> record.Id
"properties" or "props"         -> record.Metadata.TryGet<object>(Key, ...)
```

No resolution exists for: `category`, `event_type`, `event_number`, `log_position`,
`created_at`, `metadata`, `data`, `value`.

---

## 13. DuckDB Compatibility Test Schema

The current DuckDB validation test uses this table schema:

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

If WebUI-compatible aliases were added, the test table would need corresponding
columns (or aliases/generated columns) to validate expressions using those names.

---

## 14. Other Filter/Expression Systems in Proto Files

### 14.1 V2 Streams - Consistency Check Queries

```protobuf
message ConsistencyCheck {
  oneof type {
    StreamRevision revision = 1;
    Query query = 2;
  }
}
message Query {
  string expression = 1;
}
```

Server-side expression evaluated before commit. If false, transaction aborted.
Suggests a shared expression engine on the server.

### 14.2 V1 Streams - Legacy Filter Options

```protobuf
message FilterOptions {
  oneof filter {
    Expression stream_identifier = 1;
    Expression event_type = 2;
  }
  message Expression {
    string regex = 1;
    repeated string prefix = 2;
  }
}
```

Structured filter with typed `regex`/`prefix` fields. The v2 approach moves toward
a single string expression language.

### 14.3 V2 Processors - Draft Filter (Commented Out)

```protobuf
// message SubscriptionSettings {
//   string filter = 1;  // e.g., "$all", "stream_id LIKE 'orders-%'"
//   string partition_key_selector = 2;
// }
```

Uses `stream_id` (yet another column name variant) with SQL-like `LIKE` operator.
Mirrors the index definition structure (`filter` + `partition_key_selector`).

### 14.4 V2 Registry - Stream Filters

```protobuf
enum StreamFilterType {
  CONSUME_FILTER_TYPE_REGEX = 1;
  CONSUME_FILTER_TYPE_CATEGORY = 2;
}
message StreamFilter {
  StreamFilterType filter_type = 1;
  string expression = 2;
}
```

Schema group stream filtering with regex or category-based expressions.

---

## 15. Summary of All Gaps

### 15.1 Missing Columns in Client Spec

| Column | Available In | Priority |
|---|---|---|
| `category` | WebUI SQL (indexed, fast) | HIGH - commonly used |
| `event_type` (alias) | WebUI SQL | HIGH - most visible mismatch |
| `event_number` (alias) | WebUI SQL | MEDIUM |
| `log_position` (alias) | WebUI SQL | MEDIUM |
| `created_at` (alias) | WebUI SQL | LOW-MEDIUM |
| `metadata` (alias) | WebUI SQL | MEDIUM |
| `data` (body access) | WebUI SQL, planned in spec | MEDIUM - already planned |

### 15.2 Missing Access Syntax in Client Spec

| Syntax | Available In | Status in Client Spec |
|---|---|---|
| `data->>'field'` (arrow notation) | WebUI SQL | Planned, not implemented |
| `data.field` (dot notation) | Planned in spec | Planned, not implemented |
| `metadata->>'field'` | WebUI SQL | Not planned (uses `properties` instead) |
| Type casting `::TYPE` | WebUI SQL (DuckDB) | Not planned |

### 15.3 Naming Conflicts

| Client Spec Name | Conflicts With | Nature |
|---|---|---|
| `schema_name` | `event_type` (WebUI) | Different name, same concept |
| `stream_revision` | `event_number` (WebUI) | Different name, same concept |
| `position` | `log_position` (WebUI) | Ambiguous without `log_` prefix |
| `timestamp` | `created_at` (WebUI) | Different name, same concept |
| `properties` | `metadata` (WebUI) | Different name, same concept |
| `data` (planned) | `value` (rec object) | Different name, same concept |
| `sequence_id` (planned) | `sequence` (rec object) | Suffix difference |

---

## 16. Decision: Full Parity Aliases + FROM Clause

**Date:** 2026-02-17
**Decision by:** Personnel directive

### 16.1 Naming Strategy: Full Bidirectional Parity

All column names from all three systems (client spec, WebUI SQL, server `rec` object)
are recognized as equal aliases. No primary/secondary distinction. The client filter
expression spec is the **source of truth** - it defines the canonical alias table that
all systems should eventually converge on.

#### Complete Alias Table (Proposed)

| Concept | All Recognized Names | Type | Status |
|---|---|---|---|
| Event ID | `record_id`, `id` | GUID | Implemented |
| Stream name | `stream` | STRING | Implemented |
| Category | `category` | STRING | **NEW** (derived from stream) |
| Event type | `schema_name`, `schema`, `event_type` | STRING | `event_type` is **NEW** alias |
| Stream position | `stream_revision`, `revision`, `event_number` | INT64 | `event_number` is **NEW** alias |
| Log position | `position`, `pos`, `log_position` | INT64 | `log_position` is **NEW** alias |
| Timestamp | `timestamp`, `ts`, `created_at` | DATETIME | `created_at` is **NEW** alias |
| Metadata | `properties`, `props`, `metadata` | MAP | `metadata` is **NEW** alias |
| Event body | `data` | JSON | **PLANNED** (not yet implemented) |
| Schema format | `schema_format`, `format` | STRING | **PLANNED** |
| Sequence | `sequence_id`, `seq`, `sequence` | UINT64 | **PLANNED**, `sequence` is **NEW** alias |
| Redacted | `redacted` | BOOL | **PLANNED** |
| Schema ID | `schema_id` | GUID | **PLANNED** |

#### New Column: `category`

`category` is a derived column, computed from the stream name by extracting text
before the first `-` separator. This matches the WebUI SQL `category` column and the
`$idx-ce-` secondary index behavior.

```sql
-- These would be equivalent:
category = 'order'
stream LIKE 'order-%'
```

`category` would be recognized by the `ServerFilterExtractor` to map to
`StreamFilter.Prefix("$idx-ce-{value}")` for server-side pushdown.

#### Impact on PropertyAccess.Resolve()

Current resolution table:

```
"stream"                        -> record.Stream.Value
"schema_name" or "schema"       -> record.Schema.SchemaName.Value
"timestamp" or "ts"             -> record.Timestamp
"position" or "pos"             -> record.Position.LogPosition.Value
"stream_revision" or "revision" -> (long)record.StreamRevision
"record_id" or "id"             -> record.Id
"properties" or "props"         -> record.Metadata.TryGet<object>(Key, ...)
```

Proposed additions:

```
"event_type"                    -> record.Schema.SchemaName.Value  (alias for schema_name)
"event_number"                  -> (long)record.StreamRevision     (alias for stream_revision)
"log_position"                  -> record.Position.LogPosition.Value (alias for position)
"created_at"                    -> record.Timestamp                (alias for timestamp)
"metadata"                      -> record.Metadata.TryGet<object>  (alias for properties)
"category"                      -> DeriveCategory(record.Stream)   (NEW derived column)
```

#### Impact on DuckDB Validation Test Schema

Current test table:

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

Would need to expand to include alias columns (or use DuckDB generated columns):

```sql
CREATE TABLE events (
    record_id UUID,
    stream VARCHAR,
    category VARCHAR,
    schema_name VARCHAR,
    event_type VARCHAR GENERATED ALWAYS AS (schema_name),
    timestamp TIMESTAMPTZ,
    created_at TIMESTAMPTZ GENERATED ALWAYS AS (timestamp),
    position BIGINT,
    log_position BIGINT GENERATED ALWAYS AS (position),
    stream_revision BIGINT,
    event_number BIGINT GENERATED ALWAYS AS (stream_revision),
    properties MAP(VARCHAR, VARCHAR),
    metadata MAP(VARCHAR, VARCHAR) GENERATED ALWAYS AS (properties)
)
```

Note: DuckDB generated column syntax would need verification. Alternative: create
a VIEW with aliases over the base table.

### 16.2 FROM Clause: Scope Addressing

Extend the expression language with an optional `FROM` clause that maps to the
underlying `$idx-*` stream name patterns. This brings the client expression syntax
closer to the WebUI SQL.

#### Proposed Syntax

```
[FROM <source>] [WHERE] <predicate>
```

The `FROM` clause is optional. When omitted, behavior is identical to current
(filter applied to whatever read context the caller specifies). The `WHERE` keyword
is optional when a `FROM` clause is present.

#### Source Types

| Source Syntax | Maps To | Internal Stream Filter |
|---|---|---|
| `FROM stream:{name}` | Specific stream | Direct stream read |
| `FROM category:{name}` | Category index | `StreamFilter.Prefix("$idx-ce-{name}")` |
| `FROM all_events` | All events | No filter (read `$all`) |
| `FROM index:{name}` | User-defined index | `StreamFilter.Prefix("$idx-user-{name}")` |
| `FROM $all` | All events (alt) | No filter (read `$all`) |

#### Examples

```sql
-- Current syntax (still valid, FROM is optional)
stream = 'orders' AND schema_name = 'OrderPlaced'

-- With FROM clause
FROM category:order WHERE event_type = 'OrderPlaced'

-- User-defined index with field filter
FROM index:orders-by-country WHERE field_country = 'Mauritius'

-- All events with filter
FROM all_events WHERE event_type = 'OrderPlaced' AND created_at >= '2026-01-01'

-- FROM with complex predicate
FROM category:order
WHERE event_type IN ('OrderPlaced', 'OrderShipped')
  AND properties.region = 'us-east'
  AND created_at BETWEEN '2026-01-01' AND '2026-02-01'
```

#### Impact on ExpressionFilter

The `ExpressionFilter` currently has:
- `ServerFilter` (extracted stream/schema filter for server pushdown)
- `TargetStream` (derived from server filter)
- `IsMatch(Record)` (client-side predicate evaluation)

With FROM clause support:
- `FROM category:order` -> `ServerFilter = StreamFilter.Prefix("$idx-ce-order")`
- `FROM index:orders-by-country` -> `ServerFilter = StreamFilter.Prefix("$idx-user-...")`
- `FROM stream:order-123` -> `TargetStream = "order-123"` (direct stream read)
- `FROM all_events` -> `ServerFilter = RecordFilter.None`

The FROM clause and predicate-extracted server filters should be **composable**.
If a user writes `FROM category:order WHERE stream = 'order-123'`, the FROM
determines the read scope and the WHERE predicate filters within that scope.

#### User-Defined Index Field Columns (Current WebUI Convention)

The current WebUI SQL uses a `field_` prefix convention for user-defined index fields:

```sql
-- Current WebUI notation (to be supported for backwards compatibility)
FROM index:orders-by-country WHERE field_country = 'Mauritius'
```

This maps internally to `StreamFilter.Prefix("$idx-user-orders-by-country:Mauritius")`.

This `field_` prefix is a server-side implementation detail leaking into query syntax.
The ideal syntax (see Section 17) eliminates this prefix. During the transition, the
client parser should recognize both `field_country` and a cleaner alternative.

#### Virtual Table to `$idx` Mapping

The FROM clause source names are **user-friendly aliases** for the underlying `$idx-*`
stream name patterns. The client resolves them at parse time:

```
FROM category:order          -> StreamFilter.Prefix("$idx-ce-order")
FROM index:orders-by-country -> StreamFilter.Prefix("$idx-user-orders-by-country")
FROM stream:order-123        -> Direct stream read of "order-123"
FROM all_events              -> Read $all, no stream filter
FROM $all                    -> Read $all, no stream filter
```

Users should never need to write `$idx-ce-*` or `$idx-user-*` prefixes directly
in filter expressions. The FROM clause abstracts that away.

**Server-side alignment note:** The server WebUI query engine currently implements
its own virtual table resolution (`category:`, `stream:`, `index:`, `all_events`).
A future server update may adopt this spec's FROM clause syntax directly, allowing
the same expression to execute on both client and server without translation.
The `$idx-*` stream naming remains the internal transport mechanism.

#### Parser Changes Required

1. **Tokenizer**: Recognize `FROM` keyword, `:` separator in source context, `WHERE`
   keyword (currently not needed since expressions are pure predicates)
2. **Parser**: Add optional FROM clause parsing before the predicate expression.
   Grammar sketch:

   ```
   expression      := [from_clause] [where_clause] predicate
   from_clause     := FROM source
   where_clause    := WHERE
   source          := stream_source | category_source | index_source | all_source
   stream_source   := STREAM ':' identifier
   category_source := CATEGORY ':' identifier
   index_source    := INDEX ':' identifier
   all_source      := ALL_EVENTS | '$all'
   predicate       := (existing grammar unchanged)
   ```

3. **AST**: New `FilterSource` node type with source kind and name
4. **ServerFilterExtractor**: Consume `FilterSource` to produce the appropriate
   `StreamFilter`/`SchemaFilter`

### 16.3 Open Questions

1. **Should `FROM` be case-insensitive?** The WebUI SQL is case-insensitive for
   keywords. Current filter expressions treat keywords as case-insensitive
   (AND/and/And all work). Recommendation: YES, case-insensitive.

2. **Should `WHERE` be required after `FROM`?** Options:
   - `FROM category:order event_type = 'OrderPlaced'` (WHERE optional)
   - `FROM category:order WHERE event_type = 'OrderPlaced'` (WHERE required)
   Recommendation: Optional, for brevity. But allowed for readability.

3. **How to handle `field_*` columns outside of index context?** If a user writes
   `field_country = 'Mauritius'` without a `FROM index:...` clause, should it:
   - Parse error? (strict)
   - Resolve to NULL? (current behavior for unknown columns)
   - Be treated as a properties key?
   Recommendation: Resolve to NULL (consistent with current unknown column behavior).

4. **Can FROM compose with the existing ServerFilterExtractor?** If a user writes
   `FROM category:order WHERE stream = 'order-123'`, the FROM provides the read
   scope (category index) and the WHERE refines it. The extractor should not
   override the FROM-derived scope. Needs careful implementation.

5. **Stream name escaping?** Stream names can contain special characters. The
   WebUI uses `stream:order-123` unquoted. Should the client support quoted names?
   `FROM stream:'my-special.stream'`? Recommendation: Support both quoted and
   unquoted, with quoting required for names containing spaces or special chars.

---

## 17. Ideal Syntax - Target Specification (Breaking Changes)

This section defines what the filter expression language **should** look like if we
were designing it from scratch with full FROM support, unified naming, and no legacy
constraints. This is the convergence target for both the client spec and a future
server-side query syntax update.

Items marked with **[BREAKING]** change existing behavior or naming.
Items marked with **[NEW]** are additive.
Items marked with **[SERVER]** require server-side changes to align.

### 17.1 Grammar

```
query           := [from_clause] [where_clause] predicate
                 | predicate

from_clause     := FROM source

where_clause    := WHERE

source          := source_name ':' source_identifier
                 | ALL

source_name     := STREAM | CATEGORY | INDEX

source_identifier := unquoted_name | quoted_name

unquoted_name   := [a-zA-Z0-9_-]+

quoted_name     := '\'' [^']+ '\''

predicate       := (existing predicate grammar, unchanged)
```

`FROM` and `WHERE` are case-insensitive keywords. `ALL` replaces `all_events` and
`$all` as the canonical "everything" source.

### 17.2 Source Addressing

```sql
-- Specific stream
FROM stream:'order-abc123'

-- Category (maps to $idx-ce-{name})
FROM category:order

-- User-defined index (maps to $idx-user-{name})
FROM index:orders-by-country

-- All events
FROM all

-- Quoting required for names with special characters
FROM stream:'my.special/stream'
FROM category:'my-complex-category'
```

**[BREAKING]** `all_events` and `$all` become aliases for `all`. All three accepted
by the parser, but `all` is canonical.

**[SERVER]** Server WebUI would adopt the same FROM syntax instead of the current
`SELECT * FROM category:order` virtual table convention. The `FROM` in the filter
expression and the SQL `FROM` would use identical source syntax.

### 17.3 Column Names

The spec defines **canonical names** and **accepted aliases**. All resolve identically.
The canonical name is what appears in documentation and examples.

| Canonical Name | Accepted Aliases | Type | Notes |
|---|---|---|---|
| `stream` | - | STRING | Aligned across all systems |
| `category` | - | STRING | **[NEW]** Derived from stream (before first `-`) |
| `schema` | `schema_name`, `event_type` | STRING | **[BREAKING]** `schema` promoted to canonical |
| `revision` | `stream_revision`, `event_number` | INT64 | **[BREAKING]** `revision` promoted to canonical |
| `position` | `pos`, `log_position` | INT64 | `position` stays canonical, `log_position` **[NEW]** alias |
| `timestamp` | `ts`, `created_at` | DATETIME | `timestamp` stays canonical, `created_at` **[NEW]** alias |
| `id` | `record_id` | GUID | **[BREAKING]** `id` promoted to canonical |
| `properties` | `props`, `metadata` | MAP | `properties` stays canonical, `metadata` **[NEW]** alias |
| `data` | - | JSON | **[NEW]** Event body access (JSON only) |
| `format` | `schema_format` | STRING | **[NEW]** Schema serialization format |
| `sequence` | `sequence_id`, `seq` | UINT64 | **[BREAKING]** `sequence` canonical (was `sequence_id`) |
| `redacted` | - | BOOL | **[NEW]** Whether record is redacted |

**Rationale for canonical name choices:**

- `schema` over `schema_name`: shorter, matches `rec.schema.name` root.
  `event_type` kept as alias for WebUI users.
- `revision` over `stream_revision`: shorter, unambiguous in context.
  `event_number` kept as alias for WebUI users.
- `id` over `record_id`: matches `rec.id`. `record_id` kept for DuckDB test compat.
- `sequence` over `sequence_id`: matches `rec.sequence`. No `_id` suffix needed.
- `properties` over `metadata`: matches `rec.properties`. Server `rec` object is
  the tiebreaker. `metadata` kept as alias for WebUI users.

**[SERVER]** Ideal outcome: server WebUI adopts these canonical names in a future
release. The `event_type`, `event_number`, `created_at`, `metadata` names become
server-side aliases (or the WebUI column names change). The current WebUI column
names are legacy EventStoreDB terminology.

### 17.4 Property and Data Access

#### Metadata (properties)

Three syntaxes, all equivalent:

```sql
-- Bracket notation (preferred for keys with special chars)
properties['correlationId'] = 'abc-123'

-- Dot notation (convenient for simple keys)
properties.correlationId = 'abc-123'

-- Arrow notation [NEW] (WebUI SQL compatible)
properties->>'correlationId' = 'abc-123'
metadata->>'correlationId' = 'abc-123'
```

**[NEW]** Arrow notation (`->>`) for properties/metadata access. This aligns with
the WebUI SQL syntax and PostgreSQL/DuckDB JSON operators.

#### Event Body (data)

```sql
-- Dot notation
data.country = 'Mauritius'

-- Arrow notation (WebUI SQL compatible)
data->>'country' = 'Mauritius'

-- Nested access
data->'address'->>'city' = 'Port Louis'
data.address.city = 'Port Louis'
```

**[NEW]** `data` column with both dot and arrow access. Arrow notation returns text
(string). Dot notation uses automatic type coercion (existing behavior for properties).

**[SERVER]** The `rec` object uses `value` for event body. The filter expression
uses `data` (matching DuckDB/WebUI convention). Server-side expression evaluation
would map `data` to `rec.value` internally.

### 17.5 Index Field Access

**[BREAKING]** **[SERVER]** Eliminate the `field_` prefix convention.

Current WebUI (to be deprecated):
```sql
FROM index:orders-by-country WHERE field_country = 'Mauritius'
```

Ideal syntax:
```sql
FROM index:orders-by-country WHERE country = 'Mauritius'
```

When a `FROM index:{name}` source is active, the parser should resolve column names
against the index's declared field names **before** falling back to the standard
column alias table. This means `country` in the context of `index:orders-by-country`
resolves to the index field, not to an unknown column.

For the client to support this, it needs index metadata (field names and types).
Two approaches:

1. **Explicit declaration** - the user provides field metadata when creating the
   filter expression:
   ```csharp
   var filter = RecordFilter.ByExpression(
       "FROM index:orders-by-country WHERE country = 'Mauritius'",
       indexFields: new { country = IndexFieldType.String }
   );
   ```

2. **Convention-based** - any unrecognized column name in an index context is treated
   as an index field and mapped to the `$idx-user-{name}:{value}` stream prefix.

3. **Backwards-compatible** - accept both `field_country` (current) and `country`
   (ideal). The `field_` prefix is stripped if present, and the remainder is used as
   the field name. No index metadata needed.

Recommendation: **Option 3** for the client. The server can adopt option 2 or require
explicit field registration. The `field_` prefix becomes an optional, deprecated
convention rather than a requirement.

Resolution logic:
```
FROM index:orders-by-country WHERE field_country = 'Mauritius'
  -> strip "field_" prefix -> field name: "country"
  -> StreamFilter.Prefix("$idx-user-orders-by-country:Mauritius")

FROM index:orders-by-country WHERE country = 'Mauritius'
  -> "country" not in standard columns -> treat as index field
  -> StreamFilter.Prefix("$idx-user-orders-by-country:Mauritius")
```

### 17.6 Complete Examples - Ideal Syntax

#### Basic Queries

```sql
-- Filter all events by schema (simplest form, no FROM)
schema = 'OrderPlaced'

-- Same thing, explicit source
FROM all WHERE schema = 'OrderPlaced'

-- Category scope
FROM category:order WHERE schema = 'OrderPlaced'

-- Specific stream
FROM stream:'order-abc123' WHERE revision > 10
```

#### Metadata Filtering

```sql
-- Dot notation
FROM category:order
WHERE schema = 'OrderPlaced'
  AND properties.region IN ('us-east', 'us-west')
  AND properties.amount > 100

-- Arrow notation (WebUI-compatible)
FROM category:order
WHERE schema = 'OrderPlaced'
  AND metadata->>'region' IN ('us-east', 'us-west')
```

#### Data Filtering

```sql
-- JSON payload access
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->>'country' = 'Mauritius'
  AND data->>'amount' > 100

-- Nested JSON
FROM category:order
WHERE schema = 'OrderPlaced'
  AND data->'shipping'->>'method' = 'express'
```

#### User-Defined Index Queries

```sql
-- Clean field access (ideal)
FROM index:orders-by-country WHERE country = 'Mauritius'

-- With standard column filters composed
FROM index:orders-by-country
WHERE country = 'Mauritius'
  AND timestamp >= '2026-01-01'

-- Legacy field_ prefix (backwards compatible)
FROM index:orders-by-country WHERE field_country = 'Mauritius'
```

#### Complex Business Logic

```sql
FROM category:order
WHERE schema IN ('OrderPlaced', 'OrderShipped', 'OrderDelivered')
  AND properties.customerType != 'internal'
  AND properties.region = 'eu-west'
  AND data->>'amount' > 500
  AND timestamp BETWEEN '2026-01-01' AND '2026-06-30'
  AND properties.correlationId IS NOT NULL
```

#### Pattern Matching

```sql
-- Streams matching a pattern within a category
FROM category:order
WHERE stream LIKE 'order-premium-%'

-- Schema pattern
FROM all WHERE schema ILIKE '%cancelled%'

-- Category is also filterable as a predicate (not just a source)
FROM all WHERE category = 'order' AND schema = 'OrderPlaced'
```

### 17.7 Backwards Compatibility

| Current Syntax | Still Valid? | Notes |
|---|---|---|
| `stream = 'orders'` | YES | Pure predicate, no FROM needed |
| `schema_name = 'OrderPlaced'` | YES | `schema_name` is an alias for `schema` |
| `stream_revision > 10` | YES | `stream_revision` is an alias for `revision` |
| `properties['key']` | YES | Bracket notation unchanged |
| `properties.key` | YES | Dot notation unchanged |
| `field_country = 'Mauritius'` | YES | `field_` prefix accepted, stripped |

No existing valid expression breaks. All changes are additive except for
canonical name choices in documentation/examples.

### 17.8 Breaking Changes Summary

| Change | Type | Impact |
|---|---|---|
| `schema` promoted to canonical (was alias) | **DOC** | Examples/docs change, `schema_name` still works |
| `revision` promoted to canonical (was alias) | **DOC** | Examples/docs change, `stream_revision` still works |
| `id` promoted to canonical (was alias for `record_id`) | **DOC** | Examples/docs change, `record_id` still works |
| `sequence` canonical (was `sequence_id`) | **DOC** | Planned column, not yet implemented |
| `field_` prefix deprecated | **DEPRECATION** | Still accepted, just not canonical |
| `all_events`/`$all` -> `all` | **DOC** | All three accepted, `all` is canonical |
| `event_type` becomes alias, not primary | **[SERVER]** | Server WebUI would need update |
| `event_number` becomes alias, not primary | **[SERVER]** | Server WebUI would need update |
| `created_at` becomes alias, not primary | **[SERVER]** | Server WebUI would need update |
| `metadata` becomes alias for `properties` | **[SERVER]** | Server WebUI would need update |

**DOC** = documentation/example change only, no runtime breaking change.
**DEPRECATION** = old syntax still works, marked deprecated.
**[SERVER]** = requires server-side change to fully align; client supports the alias
regardless.

### 17.9 Server Alignment Roadmap

The ideal outcome is convergence: the same expression syntax works identically on
client and server. The path:

1. **Client (this spec):** Implement full alias table + FROM clause. Ship in next
   client release. No server dependency.

2. **Server WebUI:** Update DuckDB virtual table column names to support spec aliases
   alongside current names (`event_type` + `schema` both work). Add FROM clause
   parsing to the query engine. Deprecate `field_` prefix for index fields.

3. **Server index definitions:** Consider supporting the spec's expression syntax as
   an alternative to JavaScript arrow functions for index filters:
   ```
   // Current (JavaScript)
   "filter": "rec => rec.schema.name == 'OrderCreated'"

   // Future (spec expression syntax)
   "filter": "schema = 'OrderCreated'"
   ```
   This would unify the filter language across all three contexts.

4. **gRPC proto:** Align endpoint naming (`/v2/indexes/` vs `/v2/custom-indexes/`),
   field naming (`fields[]` vs `partition_key_selector`), and type enums.

---

## 18. References

### Documentation
- [KurrentDB Queries UI (v26.0)](https://docs.kurrent.io/server/v26.0/features/queries/ui.html)
- [KurrentDB User-Defined Indexes (v26.0)](https://docs.kurrent.io/server/v26.0/features/indexes/user-defined.html)
- [KurrentDB Secondary Indexes (v25.1)](https://docs.kurrent.io/server/v25.1/features/indexes/secondary.html)
- [Server-Side Filtering Blog](https://www.kurrent.io/blog/server-side-filtering/)
- [Secondary Indexes Blog](https://www.kurrent.io/blog/secondary-indexes/)

### Local Files
- Filter spec: `.project/docs/features/filter-expressions-spec.md`
- Research notes: `.project/docs/research/filter-expressions.md`
- ADR: `.project/docs/adr/0001-filter-expression-parser-architecture.md`
- Parser: `src/.../Modules/Streams/Models/Filters/ExpressionFilter.Parser.cs`
- Server extractor: `src/.../Modules/Streams/Models/Filters/ExpressionFilter.ServerExtractor.cs`
- Record filter: `src/.../Modules/Streams/Models/Filters/RecordFilter.cs`
- Indexes proto: `proto/kurrentdb/protocol/v2/indexes/indexes.proto`
- V2 streams proto: `proto/kurrentdb/protocol/v2/streams/streams.proto`
- DuckDB validation tests: `test/.../Filters/ExpressionFilter.DuckDbValidationTests.cs`
- Parser tests: `test/.../Filters/ExpressionFilterParserTests.cs`
