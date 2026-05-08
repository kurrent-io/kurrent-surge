---
order: 4
---

# Filters

<CopyMarkdownButton />

Filters narrow the set of records returned from a read. They work against named streams, the global log, and secondary index streams. The same `IRecordFilter` types apply across `ReadStream` and `ReadRecords` (and their request builders).

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();

var stream = client.Streams.ReadRecords(request);
```

## Factory methods

`RecordFilter` is the entry point. Each factory returns an `IRecordFilter` you pass to `WithFilter`.

| Factory | Targets | Description |
|---|---|---|
| `RecordFilter.None` | `$all` | Matches every record. |
| `RecordFilter.ByStream(string)` | named stream | Exact match on a single stream. Rejects names that start with `$idx-`. |
| `RecordFilter.ByCategory(string)` | `$idx-ce-{category}` | All streams sharing a category prefix (KurrentDB stream names follow the convention `category-id`). |
| `RecordFilter.BySchema(string)` | `$idx-et-{schema}` | All records of a given schema name. |
| `RecordFilter.ByUserIndex(string)` | `$idx-user-{name}` | A user-defined secondary index. |
| `RecordFilter.ByStreamPattern(string)` | `$all` | Regex match on stream names. Rejects patterns that target index streams. |
| `RecordFilter.ByExpression(string)` | depends on expression | DuckDB-compatible filter expression. The client extracts a server-side component when possible. |

`ByCategory`, `BySchema`, and `ByUserIndex` route through the server's secondary-index dispatch path. They are the recommended way to read across many streams and significantly cheaper than scanning `$all`.

## Stream filters

```csharp
RecordFilter.ByStream("order-123");
```

Sets `TargetStream`, so the client routes the read directly to the named stream.

To match by pattern instead:

```csharp
var filter = RecordFilter.ByStreamPattern(@"^order-\d+$");
```

Patterns are standard .NET regex. Use `(?i)` for case-insensitive matching. Anchored literal patterns (e.g. `^order-123$`) are detected and routed as direct stream reads. Wildcards and alternations fall back to `$all` with client-side matching.

## Index filters

For multi-stream reads, use the index helpers instead of building patterns by hand.

```csharp
// All records in streams matching "order-*"
var byCategory = RecordFilter.ByCategory("order");

// All OrderPlaced records across every stream
var byType = RecordFilter.BySchema("OrderPlaced");

// A user-defined secondary index
var byUserIndex = RecordFilter.ByUserIndex("HighValueOrders");
```

Passing an `$idx-*` stream name to `ByStream` or `ByStreamPattern` throws `ArgumentException` with a redirect to the correct factory. Use the factories directly.

## Expression filters

`ByExpression` takes a DuckDB-style SQL expression. The expression can reference stream metadata, the decoded payload, and record metadata.

```csharp
var filter = RecordFilter.ByExpression(
    "stream = 'order-123' AND schema_name = 'OrderPlaced'"
);

var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(filter)
    .Build();
```

The expression is parsed at construction. The client analyzes the AST and pushes down what the server can evaluate (typically stream and schema predicates), running the rest client-side after decoding.

### Property aliases

Expressions reference record properties by alias. The standard map:

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

When an expression references `data.*`, decoding must be enabled (the default). Setting `SkipDecoding()` on the request makes payload predicates always fail.

## Filter expressions on the request builder

You can pass an expression string directly without building a filter object:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilterExpression("stream like 'order-%' AND schema_name = 'OrderPlaced'")
    .Build();
```

`WithFilterExpression` is shorthand for `WithFilter(RecordFilter.ByExpression(...))`.

## Inspecting a filter

Every filter exposes the original expression and the target stream it resolved to:

```csharp
var filter = RecordFilter.ByCategory("order");

Console.WriteLine("Expression:    {0}", filter.Expression);
Console.WriteLine("Target stream: {0}", filter.TargetStream);
Console.WriteLine("Index filter:  {0}", filter.IsIndexFilter);
```

`IsStreamFilter` is true when the filter resolves to a single stream (named or index). `IsIndexFilter` is true when that stream is a `$idx-*` index. Reading code uses these flags to route requests through the right server endpoint.

## Combining filters with reads

Filters apply uniformly across read APIs.

```csharp
// Stream read, narrowed by schema name
var streamRequest = ReadStreamRequest.New
    .ForStream("order-123")
    .WithFilter(RecordFilter.BySchema("OrderPlaced"))
    .Build();

// Global log, narrowed by category
var logRequest = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(RecordFilter.ByCategory("order"))
    .Build();
```

When a filter targets a single stream (named or index), `ReadRecords` and `ReadStream` collapse to a direct stream read on the resolved `TargetStream`. No special handling is required from the caller.
