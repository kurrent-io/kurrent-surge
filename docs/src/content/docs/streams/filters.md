---
title: Filters
description: Narrow a read or a subscription server-side, by stream, by schema, or by an expression.
---

Reading the whole log and discarding most of it costs the server the scan and you the bandwidth. A
filter moves that decision to the server, so only matching records travel.

```csharp
var options = new ReadRecordsOptions {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.ByCategory("orders")
};
```

`RecordFilter.ExcludeSystemRecords` is the default. It drops every record whose stream or schema name
starts with `$`, the records the server and the projections write for themselves. `RecordFilter.None`
is the raw log.

## Choosing a filter

| Factory | Matches | Backed by |
|---|---|---|
| `ByCategory(name)` | Every stream in a category | `$idx-ce-{category}` |
| `BySchema(name)` | Every record of one schema | `$idx-et-{schema}` |
| `ByUserIndex(name)` | Whatever a [secondary index](/kurrent-surge/client/indexes/) contains | `$idx-user-{name}` |
| `ByStream(name)` | One stream, named exactly | direct stream read |
| `ByStreamPattern(regex)` | Streams whose name matches | scan |
| `ByExpression(sql)` | Anything expressible in SQL | scan, or an index when it can be extracted |

The first three read an index the server already maintains, which is why they are the cheapest. Reach
for them before an expression that says the same thing.

```csharp
RecordFilter.ByCategory("orders")                    // cheap, index-backed
RecordFilter.ByExpression("category = 'orders'")     // same result, scans
```

`ByStream` and `ByStreamPattern` reject index stream names. Pass `$idx-user-ORDERS` to either and it
throws, because the index-backed factories exist for that.

## Expressions

`ByExpression` takes DuckDB-compatible SQL. Only the predicate, with no `SELECT` and no `WHERE`.

```csharp
RecordFilter.ByExpression("schema_name = 'OrderPlaced' AND data.amount > 1000")
```

### Columns

| Column | Aliases | Holds |
|---|---|---|
| `stream` | | The full stream name |
| `category` | | The category part of the stream name |
| `stream_revision` | `revision` | Position within the stream |
| `log_position` | `position`, `pos` | Position in the global log |
| `schema_name` | `schema` | The record's schema name |
| `schema_id` | | The registered schema version id |
| `schema_format` | | `Json`, `Protobuf`, `Avro` or `Bytes` |
| `created_at` | `timestamp`, `ts` | Unix milliseconds |
| `record_id` | | The record id, as 16 bytes |
| `data` | | The decoded payload, addressed by path |
| `metadata` | `properties`, `props` | Custom metadata, addressed by key |

`data` and `metadata` are addressed with dots: `data.customerId`, `data.shipping.country`,
`props.tenant`.

`record_id` is a BLOB, so its SQL faces come from DuckDB's own functions rather than anything
id-specific: `hex(record_id)` for the hex form, `CAST(record_id AS UUID)` for the GUID.

### Parameters

Bind values rather than splicing them into the string. A bound value becomes a literal in the parsed
expression, not text.

```csharp
RecordFilter.ByExpression(
    "stream = @stream AND revision > @from",
    new FilterParameters()
        .Add("stream", "order-123")
        .Add("from", 100));
```

`Add` is typed. There are overloads for `string`, the numeric types, `Guid`, `Enum`, and for the
domain types `StreamName`, `LogPosition`, `Revision` and `RecordId`, so a value binds as itself
rather than as its text.

## Where a filter runs

A filter the server understands is applied there, and the client receives only matches. A filter the
server cannot fully express is applied twice: the server narrows what it can, and the client
evaluates the remainder.

That second case has a consequence worth knowing. Records the client discards were still read, so a
subscription would appear to stall over a long stretch of non-matching data. The client raises
synthetic scan checkpoints across those gaps, which is why a filtered read reports progress even when
nothing matches.

:::note
An expression whose only stream constraint is exact equality resolves to a single-stream read rather
than a filtered scan of `$all`. That is faster, and it is also narrower than some readers expect.
Use `LIKE`, `IN`, `OR`, or a compound predicate when you mean to scan the log.
:::

`FilterMaxSearchWindow` sets how many raw records the server scans per page before emitting a
checkpoint and returning. It applies only when a filter routes the read through `$all`.

## Reading an index directly

A populated index is a stream, so it can also be read by name.

```csharp
var options = new ReadRecordsOptions {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.ByExpression(
        "stream = '$idx-user-ORDERS_BY_CUSTOMER' AND data.amount > 1000")
};
```

That combines the index with a predicate on its columns. `ByUserIndex` alone returns everything the
index holds.
