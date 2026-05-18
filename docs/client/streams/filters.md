---
order: 4
---

# Filters

<CopyMarkdownButton />

Filters narrow the records returned from a read. The same filter types apply to `ReadRecords` (global log) and `ReadStream` (single stream), and to subscriptions built on top of them.

```csharp
var request = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter(RecordFilter.ByCategory("order"))
    .Build();

await foreach (var item in client.Streams.ReadRecords(request)) {
    // ...
}
```

## Factory methods

`RecordFilter` is the entry point. Each factory returns an `IRecordFilter` you pass to `Filter`.

| Factory | Resolves to | Description |
|---|---|---|
| `None` | `$all` | Matches every record. |
| `ByStream(name)` | named stream | Exact match on a single stream. Rejects names starting with `$idx-`. |
| `ByCategory(category)` | `$idx-ce-{category}` | All streams sharing a category prefix. |
| `BySchema(schema)` | `$idx-et-{schema}` | All records of a given schema name. |
| `ByUserIndex(name)` | `$idx-user-{name}` | A user-defined secondary index. |
| `ByStreamPattern(regex)` | `$all` | Regex match on stream names. Rejects patterns targeting index streams. |
| `ByExpression(sql)` | depends on expression | DuckDB-compatible SQL expression. The client pushes down what the server can evaluate. |

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

var request = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter(filter)
    .Build();
```

The expression is parsed at construction. The client analyzes the AST, pushes down stream and schema predicates the server can evaluate, and runs the rest client-side after decoding.

To pass the expression directly:

```csharp
var request = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter("stream LIKE 'order-%' AND schema_name = 'OrderPlaced'")
    .Build();
```

The string overload is shorthand for `Filter(RecordFilter.ByExpression(...))`.

When an expression references `data.*`, decoding must remain enabled (the default). `SkipDecoding()` makes payload predicates always evaluate to false.

## Expression properties

Expressions reference record properties by alias.

| Alias | Source |
|---|---|
| `record_id` | `record.Id` |
| `log_position`, `position`, `pos` | `record.Position.LogPosition` |
| `stream` | `record.Stream` |
| `category` | `record.Stream.Category` |
| `stream_revision`, `revision` | `record.Revision` |
| `schema_name`, `schema` | `record.Schema.SchemaName` |
| `schema_id` | `record.Schema.SchemaVersionId` |
| `schema_format` | `record.Schema.SchemaFormat` |
| `created_at`, `timestamp`, `ts` | `record.Timestamp` (Unix milliseconds) |
| `data.<path>` | dot-separated path into the decoded JSON payload |
| `metadata.<key>`, `properties.<key>`, `props.<key>` | keyed lookup into `record.Metadata` |

```csharp
RecordFilter.ByExpression("category = 'order' AND data.amount > 100");
RecordFilter.ByExpression("props.tenant = 'acme' AND ts > 1700000000000");
RecordFilter.ByExpression("data.address.city = 'Berlin'");
```

## Filters on stream reads

`ReadStreamRequest.Filter` accepts a filter only if it does not contradict the stream being read. Allowed combinations:

| Filter | Allowed on `ReadStreamRequest` |
|---|---|
| `RecordFilter.None` | Yes |
| `ByStream(s)` where `s` matches `Stream(s)` | Yes |
| `ByExpression` whose server filter resolves to the same stream | Yes |
| Any filter targeting a different stream or `$all` | No, fails validation at `Build()` |

For multi-stream and index reads, use `ReadRecordsRequest`. The client collapses index-targeted requests into a direct read on the resolved index stream.

```csharp
var request = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter(RecordFilter.BySchema("OrderPlaced"))
    .Build();
```

## Inspecting a filter

Every filter exposes read-only properties used by request shaping.

| Property | Description |
|---|---|
| `Expression` | The regex pattern (`StreamFilter`) or SQL string (`ExpressionFilter`) the filter evaluates. |
| `TargetStream` | The stream the filter resolves to. `$all` for filters without a single target. |
| `IsStreamFilter` | True when the filter resolves to a single stream (named or index). |
| `IsIndexFilter` | True when that stream is a `$idx-*` index. |

```csharp
var filter = RecordFilter.ByCategory("order");

Console.WriteLine("Target: {0}", filter.TargetStream);  // $idx-ce-order
Console.WriteLine("Index?  {0}", filter.IsIndexFilter); // True
```
