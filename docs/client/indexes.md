# Indexes

<CopyMarkdownButton />

Secondary indexes let you query records by a payload field instead of by stream name. You define an index with a filter and a list of fields to project, then read the matching records back through the standard read APIs.

Access the module through `client.Indexes`:

```csharp
var indexes = client.Indexes;
```

Operations throw on failure. See [Error handling](./error-handling.md) for the exception catalogue.

## Create an index

```csharp
await client.Indexes.Create(
    name: "ORDERS_BY_CUSTOMER",
    filter: "schema_name = 'OrderPlaced'",
    fields: [
        new IndexField("customer_id", "data.customerId", IndexFieldType.String),
        new IndexField("amount",      "data.amount",     IndexFieldType.Double)
    ]
);
```

The index is created and started by default. Pass `start: false` to defer.

| Parameter | Type | Description |
|---|---|---|
| `name` | `string` | Unique index name. Use uppercase letters, digits, and underscores (1-63 chars, must start with a letter and end alphanumerically). |
| `filter` | `string` | DuckDB-compatible expression deciding which records the index contains. Same grammar as [filter expressions](./streams/filters.md#expression-filters). |
| `fields` | `IEnumerable<IndexField>` | Columns to index. Each has a name, a JSON selector path, and a type. |
| `start` | `bool?` | Whether to start the index after creation. Defaults to `true` server-side. |

## Field types

| `IndexFieldType` | Maps to |
|---|---|
| `String` | `string` |
| `Double` | `double` |
| `Int32` | `int` |
| `Int64` | `long` |

Selector paths are dot-separated and resolve against the decoded JSON payload. `data.customerId` reads `customerId` at the root. `data.shipping.country` walks two levels into the payload.

## Read from an index

A populated index exposes itself as a stream named `$idx-user-{name}`. Use `RecordFilter.ByUserIndex` to read it:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(RecordFilter.ByUserIndex("ORDERS_BY_CUSTOMER"))
    .Build();

var stream = client.Streams.ReadRecords(request);
```

Pair the index filter with a payload predicate to pre-filter on the indexed columns:

```csharp
var request = ReadRecordsRequest.New
    .FromPosition(LogPosition.Earliest)
    .WithFilter(RecordFilter.ByUserIndex("ORDERS_BY_CUSTOMER"))
    .WithFilterExpression("data.amount > 1000")
    .Build();
```

See [Filters](./streams/filters.md) for the full filter expression grammar.

## List indexes

```csharp
var indexes = await client.Indexes.List();

foreach (var index in indexes) {
    Console.WriteLine("{0,-30} {1}", index.Name, index.State);
    Console.WriteLine("  filter: {0}", index.Filter);
    Console.WriteLine("  fields: {0}", string.Join(", ", index.Fields.Select(f => f.Name)));
}
```

## Get an index

Fetch a single index by name:

```csharp
var index = await client.Indexes.Get("ORDERS_BY_CUSTOMER");
```

Both methods return the same `Index` shape:

| Property | Type | Description |
|---|---|---|
| `Name` | `string` | Index name. |
| `Filter` | `string` | The original filter expression. |
| `Fields` | `IReadOnlyList<IndexField>` | Indexed columns. |
| `State` | `IndexState` | `Unspecified`, `Started`, `Stopped`, or `Deleted`. |

## Lifecycle

Indexes go through `Started → Stopped → Deleted`. The lifecycle methods accept the index name:

```csharp
await client.Indexes.Stop("ORDERS_BY_CUSTOMER");
await client.Indexes.Start("ORDERS_BY_CUSTOMER");
await client.Indexes.Delete("ORDERS_BY_CUSTOMER");
```

| Method | State after | Notes |
|---|---|---|
| `Create(...)` | `Started` (or `Stopped` if `start: false`) | |
| `Start(name)` | `Started` | |
| `Stop(name)` | `Stopped` | Pauses ingestion. Resumable. |
| `Delete(name)` | `Deleted` | Permanent. |
