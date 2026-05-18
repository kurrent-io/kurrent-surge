# Indexes

<CopyMarkdownButton />

Secondary indexes let you query records by a payload field instead of by stream name. Define one with a filter and a list of fields to project, and read the matching records back through the standard read APIs.

Access the module through `client.Indexes`:

```csharp
var indexes = client.Indexes;
```

Operations throw on failure. See [Error handling](./error-handling.md) for the exception catalogue.

## Create an index

Build a `CreateIndexRequest` and pass it to the client.

```csharp
await client.Indexes.Create(
    CreateIndexRequest.For("ORDERS_BY_CUSTOMER")
        .WithFilter("schema_name = 'OrderPlaced'")
        .WithField("customer_id", "data.customerId", IndexFieldType.String)
        .WithField("amount",      "data.amount",     IndexFieldType.Double)
        .Build());
```

The server starts the index immediately by default. Call `.WithStart(false)` to create it stopped.

| Method                                       | Description                                                  |
|----------------------------------------------|--------------------------------------------------------------|
| `For(IndexName)`                             | Start the request for the given index name.                  |
| `WithFilter(string)`                         | DuckDB-compatible expression deciding which records the index contains. Same grammar as [filter expressions](./streams/filters.md#expression-filters). |
| `WithField(string, string, IndexFieldType)`  | Add one indexed column by name, selector path, and type.     |
| `WithField(IndexField)`                      | Add a pre-built `IndexField`.                                |
| `WithFields(IEnumerable<IndexField>)`        | Replace the field list.                                      |
| `WithStart(bool)`                            | Override the server default. Omit to start immediately.      |

::: tip
For the common case of name, filter, and fields, an overload skips the builder. A second one takes a trailing `bool start`.

```csharp
await client.Indexes.Create(
    "ORDERS_BY_CUSTOMER",
    "schema_name = 'OrderPlaced'",
    [
        new IndexField("customer_id", "data.customerId", IndexFieldType.String),
        new IndexField("amount",      "data.amount",     IndexFieldType.Double)
    ]);
```
:::

## Field types

| `IndexFieldType` | Maps to |
|---|---|
| `String` | `string` |
| `Double` | `double` |
| `Int32` | `int` |
| `Int64` | `long` |

Selector paths are dot-separated and resolve against the decoded JSON payload. `data.customerId` reads `customerId` at the root. `data.shipping.country` walks two levels into the payload.

## Read from an index

A populated index exposes itself as a stream named `$idx-user-{name}`. Use `RecordFilter.ByUserIndex` to read it. To pre-filter on the indexed columns, pass a single expression that targets the index stream and the payload fields:

```csharp
var all = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter(RecordFilter.ByUserIndex("ORDERS_BY_CUSTOMER"))
    .Build();

var highValue = ReadRecordsRequest.New()
    .FromPosition(LogPosition.Earliest)
    .Filter("stream = '$idx-user-ORDERS_BY_CUSTOMER' AND data.amount > 1000")
    .Build();

var stream = client.Streams.ReadRecords(highValue);
```

See [Filters](./streams/filters.md) for the full filter expression grammar.

## Inspect indexes

`List` returns every registered index. `Get` fetches a single one by name.

```csharp
var indexes = await client.Indexes.List();

foreach (var index in indexes) {
    Console.WriteLine("{0,-30} {1}", index.Name, index.State);
    Console.WriteLine("  filter: {0}", index.Filter);
    Console.WriteLine("  fields: {0}", string.Join(", ", index.Fields.Select(f => f.Name)));
}

var orders = await client.Indexes.Get("ORDERS_BY_CUSTOMER");
```

Both return the same `Index` shape:

| Property | Type | Description |
|---|---|---|
| `Name` | `IndexName` | Index name. |
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
| `Start(name)` | `Started` | |
| `Stop(name)` | `Stopped` | Pauses ingestion. Resumable. |
| `Delete(name)` | `Deleted` | Permanent. |
