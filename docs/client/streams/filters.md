---
order: 4
---

# Filters

<CopyMarkdownButton />

Filters narrow the records returned from a read. The same filter types apply to `ReadRecords` (global log) and `ReadStream` (single stream), and to subscriptions built on top of them.

```csharp
var request = new ReadRecordsRequest {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.ByCategory("order")
};

await foreach (var item in client.Streams.ReadRecords(request)) {
    // ...
}
```

## Factory methods

`RecordFilter` is the entry point. Each factory returns a `RecordFilter` you assign to `Filter`.

| Factory | Resolves to | Description |
|---|---|---|
| `None` | `$all` | Matches every record. |
| `ByStream(name)` | named stream | Exact match on a single stream. Rejects names starting with `$idx-`. |
| `ByCategory(category)` | `$idx-ce-{category}` | All streams sharing a category prefix. |
| `BySchema(schema)` | `$idx-et-{schema}` | All records of a given schema name. |
| `ByUserIndex(name)` | `$idx-user-{name}` | A user-defined secondary index. |
| `ByStreamPattern(regex)` | `$all` | Regex match on stream names. Rejects patterns targeting index streams. |
| `ByExpression(sql)` | depends on expression | DuckDB-compatible SQL expression. The client pushes down what the server can evaluate. |
| `ByExpression(sql, parameters)` | depends on expression | The same, with `@name` placeholders bound to values. |

`ByCategory`, `BySchema`, and `ByUserIndex` route through the server's secondary-index dispatch path. They are the right way to read across many streams and significantly cheaper than scanning `$all`.

## Match a single stream

```csharp
var filter = RecordFilter.ByStream("order-123");
```

The filter resolves to the named stream, so reads route directly to it without scanning `$all`.

## Match across streams

For multi-stream reads, prefer the index helpers over hand-built patterns.

```csharp
var byCategory  = RecordFilter.ByCategory("order");           // all "order-*" streams
var bySchema    = RecordFilter.BySchema("OrderPlaced");       // all OrderPlaced records
var byUserIndex = RecordFilter.ByUserIndex("HighValueOrders");
```

Passing an `$idx-*` stream name to `ByStream` or `ByStreamPattern` throws `ArgumentException` with a redirect to the correct factory.

## Match by regex

Use `ByStreamPattern` only when an index helper does not fit. Patterns are standard .NET regex.

```csharp
var filter = RecordFilter.ByStreamPattern(@"^order-\d+$");
```

Anchored literal patterns (`^order-123$`) are recognized and routed as direct stream reads. Wildcards, alternation, and case-insensitive modifiers fall back to `$all` with client-side matching. Use `(?i)` for case-insensitive matches.

## Match by expression

`ByExpression` takes a DuckDB-compatible SQL expression. The expression can reference stream metadata, the decoded payload, and record metadata.

```csharp
var filter = RecordFilter.ByExpression(
    "stream = 'order-123' AND schema_name = 'OrderPlaced'"
);

var request = new ReadRecordsRequest {
    Start  = LogPosition.Earliest,
    Filter = filter
};
```

The expression is parsed at construction. The client works out what the server can narrow — a stream, a category, or a schema — sends that, and evaluates the whole expression against each record that comes back.

Naming an index stream is an ordinary stream predicate, so this reads through the index and filters the payload client-side:

```csharp
RecordFilter.ByExpression("stream = '$idx-user-orders-by-country' AND data.amount > 1000");
```

`RecordFilter.ByExpression` also takes the expression directly:

```csharp
var request = new ReadRecordsRequest {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.ByExpression("stream LIKE 'order-%' AND schema_name = 'OrderPlaced'")
};
```

When an expression references `data.*`, decoding must remain enabled (the default). Setting `SkipDecoding = true` makes payload predicates always evaluate to false.

## Expression properties

Expressions reference record properties by alias.

| Alias | Source |
|---|---|
| `record_id` | `record.Id`, as its 16 bytes (a `BLOB`) |
| `log_position`, `position`, `pos` | `record.Position.LogPosition` |
| `stream` | `record.Stream` |
| `category` | `record.Stream.Category` |
| `stream_revision`, `revision` | `record.Revision` |
| `schema_name`, `schema` | `record.Schema.SchemaName` |
| `schema_id` | `record.Schema.SchemaVersionId` |
| `schema_format` | `record.Schema.SchemaFormat` |
| `created_at`, `timestamp`, `ts` | `record.Timestamp` (Unix milliseconds) |
| `data` | the decoded JSON payload |
| `metadata`, `properties`, `props` | `record.Metadata` |

```csharp
RecordFilter.ByExpression("category = 'order' AND data.amount > 100");
RecordFilter.ByExpression("props.tenant = 'acme' AND ts > 1700000000000");
RecordFilter.ByExpression("data.address.city = 'Berlin'");
```

## Reaching into JSON

`data` and the metadata aliases are documents. Reach a member five ways, all of which DuckDB accepts:

| Written | Reads |
|---|---|
| `data.address.city` | descends two levels |
| `data->>'$.address.city'` | descends, JSONPath |
| `data->>'/address/city'` | descends, JSON Pointer |
| `data->>'city'` | the member named `city` |
| `data['city']` | the member named `city` |

A key inside `->>` is a member name, not a path. `data->>'address.city'` reads a member literally called `address.city` and finds nothing unless one exists. Use `$.` or `/` to descend.

JSONPath indexes arrays from zero: `data->>'$.tags[0]'`, or `data->>'/tags/0'`.

Dot notation is client syntax. It reaches the server as `data->>'$.address.city'`, so it means the same thing on both sides.

## Calling functions

An expression may call DuckDB's functions on a column, or on a literal.

```csharp
RecordFilter.ByExpression("lower(stream) = 'order-123'");
RecordFilter.ByExpression("lower(props.region) = 'us-east'");
RecordFilter.ByExpression("length(schema_name) > 5");
RecordFilter.ByExpression("starts_with(stream, 'order') = true");
```

| Family | Functions |
|---|---|
| Text | `lower` `upper` `trim` `ltrim` `rtrim` `length` |
| Predicates | `starts_with` `ends_with` `contains` |
| Bytes | `hex` `from_hex` |
| Time | `epoch_ms` |

A call on a **literal** is evaluated once when the expression is parsed, so it costs nothing per record:

```csharp
RecordFilter.ByExpression("created_at > epoch_ms(TIMESTAMP '2026-01-01')");
```

A call on a **column** prevents server-side narrowing. `lower(stream) = 'order-1'` cannot be pushed down, so the read scans `$all` and filters on the client. Compare the column directly when you can.

It also costs more per record. Measured with BenchmarkDotNet on .NET 10:

| Expression | Per record | Allocated |
|---|---|---|
| `stream = 'orders-123'` | 7.9 ns | 0 B |
| `CAST(revision AS VARCHAR) = '42'` | 19.4 ns | 24 B |
| `lower(stream) = 'orders-123'` | 33.5 ns | 48 B |
| `lower(trim(stream)) = 'orders-123'` | 35.2 ns | 48 B |

A bare comparison on a typed column allocates nothing. A transform costs one box per step, because the
value travels through the chain untyped.

## Casting

`CAST(x AS T)` and `x::T` are the same thing. `TRY_CAST` yields `NULL` instead of failing.

```csharp
RecordFilter.ByExpression("CAST(revision AS VARCHAR) = '3'");
RecordFilter.ByExpression("revision::VARCHAR = '3'");
RecordFilter.ByExpression("TRY_CAST(props.amount AS BIGINT) > 100");
```

Targets: `VARCHAR` `BIGINT` `INTEGER` `DOUBLE` `DECIMAL` `BOOLEAN` `UUID` `BLOB` `TIMESTAMP` `DATE`.

A failed `CAST` raises `FilterConversionException` while the read is running, matching DuckDB, where a bad cast fails the query. Use `TRY_CAST` when a column legitimately holds values that will not convert.

## Matching a record id

`record_id` is a `BLOB` of 16 bytes. Two ways to name one:

```csharp
var id = record.Id;

// By bytes. Preferred — nothing is converted per record.
RecordFilter.ByExpression($"record_id = from_hex('{Convert.ToHexString(id.ToByteArray())}')");

// By its GUID face, which reads better in a log or an issue.
RecordFilter.ByExpression($"CAST(record_id AS UUID) = '{id.ToGuid()}'");
```

`hex(record_id) = '…'` also works, but applies a function to every record rather than converting the literal once.

## Binding values

Build an expression with `@name` placeholders and bind the values, rather than interpolating them.

```csharp
var filter = RecordFilter.ByExpression(
    "stream = @s AND revision > @from AND record_id = @id",
    new FilterParameters()
        .Add("s",    stream)
        .Add("from", revision)
        .Add("id",   record.Id));
```

Each `Add` takes the value at its own type and formats it for you, so you do not need to know that
`record_id` wants `from_hex(...)` or that `created_at` wants Unix milliseconds:

| Bound type | Becomes |
|---|---|
| `string`, `StreamName`, `SchemaName` | quoted text, escaped |
| `long`, `int`, `decimal`, `double`, `LogPosition`, `Revision` | number |
| `bool` | `TRUE` / `FALSE` |
| `Guid` | dashed text, for `CAST(record_id AS UUID) = @id` |
| `RecordId`, `byte[]` | blob, for `record_id = @id` |
| `DateTimeOffset`, `DateTime` | Unix milliseconds |
| `null` | `NULL` |

A bound value never becomes part of the expression text — it resolves to a literal in the parsed
tree — so a value cannot alter the expression it appears in:

```csharp
.Add("s", "o' OR 1=1 --")   // just a stream name that matches nothing
```

Names are case-insensitive and `Add("@s", …)` works as well as `Add("s", …)`. Binding a name twice
throws. Referencing a name nothing is bound to, or binding one the expression never names, is a parse
error — both are usually a typo.

## Writing literals by hand

You can still write the literals yourself. What goes where:

| Column | Literal |
|---|---|
| `stream`, `category`, `schema_name`, `schema_id`, `schema_format` | quoted — `'order-123'` |
| `log_position`, `stream_revision`, `created_at` | bare number, or `epoch_ms(TIMESTAMP '…')` |
| `record_id` | `from_hex('…')`, or cast the column to `UUID` |
| `data`, `metadata` / `props` | quoted — members read as text |

```csharp
// A stream and a revision.
RecordFilter.ByExpression($"stream = '{stream}' AND revision = {revision}");

// Everything from the last year. created_at is Unix milliseconds.
var since = DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeMilliseconds();
RecordFilter.ByExpression($"created_at > {since}");
```

A value carrying an apostrophe has to be escaped with a backslash, or it ends the string early:

```csharp
RecordFilter.ByExpression($"stream = '{stream.Replace("'", "\\'")}'");
```

## What is rejected

Type rules follow DuckDB, and both failures are raised by `ByExpression` when it parses — not silently at read time.

| Written | Why |
|---|---|
| `created_at > '2026-01-01'` | A date string is not a number. Use `epoch_ms(TIMESTAMP '2026-01-01')` or Unix milliseconds |
| `stream > 5` | A number cannot compare against a text column. Quote it, or cast the column |
| `stream = true` | Same rule, for booleans |
| `revision > 'abc'` | The text does not convert to a number |

Earlier versions answered these instead of refusing, and the answer was arbitrary — `created_at < 'abc'` matched every record because it compared `"1718409600000"` against `"abc"` as text.

## Filters on stream reads

`ReadStreamRequest.Filter` accepts a filter only if it does not contradict the stream being read. Allowed combinations:

| Filter | Allowed on `ReadStreamRequest` |
|---|---|
| `RecordFilter.None` | Yes |
| `ByStream(s)` where `s` matches `Stream(s)` | Yes |
| `ByExpression` whose server filter resolves to the same stream | Yes |
| Any filter targeting a different stream or `$all` | No, fails validation when the request is sent |

For multi-stream and index reads, use `ReadRecordsRequest`. The client collapses index-targeted requests into a direct read on the resolved index stream.

```csharp
var request = new ReadRecordsRequest {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.BySchema("OrderPlaced")
};
```

## Inspecting a filter

Every `RecordFilter` exposes two members regardless of concrete type:

| Member | Description |
|---|---|
| `Expression` | The original input the filter was built from — stream name, regex pattern, category/schema/index name, or SQL string. |
| `IsMatch(record)` | Evaluates a record against the filter (client-side). |

```csharp
RecordFilter filter = RecordFilter.ByCategory("order");

Console.WriteLine(filter.Expression); // order
```

For kind-specific details, match on the concrete type:

| Type | Extra members |
|---|---|
| `StreamNameFilter` | `TargetStream` — the single stream the read routes to. |
| `CategoryFilter` | `CategoryName` — the resolved category. |
| `SchemaFilter` | `SchemaName` — the resolved schema (event type) name. |
| `ExpressionFilter`, `StreamRegexFilter`, `UserIndexFilter`, `EmptyFilter` | no extra public members. |

```csharp
switch (filter) {
    case StreamNameFilter s: Console.WriteLine($"Direct read on {s.TargetStream}"); break;
    case CategoryFilter c:   Console.WriteLine($"Category {c.CategoryName}");        break;
    case SchemaFilter sf:    Console.WriteLine($"Schema {sf.SchemaName}");           break;
}
```
