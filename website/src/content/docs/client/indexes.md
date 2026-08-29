---
title: Indexes
description: Query records by a payload field instead of by stream name.
---

A secondary index lets you find records by a field inside the payload rather than by stream name. You
define it with a filter and a list of fields to project; the server maintains it, and you read the
result back through the normal stream APIs.


## Create an index

```csharp
await client.Indexes.Create(new CreateIndexOptions {
    Name   = "ORDERS_BY_CUSTOMER",
    Filter = "schema_name = 'OrderPlaced'",
    Fields = [
        new IndexField("customer_id", "data.customerId", IndexFieldType.String),
        new IndexField("amount",      "data.amount",     IndexFieldType.Double)
    ]
});
```

The short form skips the options type for the common case:

```csharp
await client.Indexes.Create(
    "ORDERS_BY_CUSTOMER",
    "schema_name = 'OrderPlaced'",
    [
        new IndexField("customer_id", "data.customerId", IndexFieldType.String),
        new IndexField("amount",      "data.amount",     IndexFieldType.Double)
    ]);
```

| Option | Type | Meaning |
|---|---|---|
| `Name` | `IndexName` | Unique |
| `Filter` | `string` | Decides which records the index contains. Same grammar as a [read filter](/client/streams/filters/) |
| `Fields` | `IList<IndexField>` | The indexed columns |
| `Start` | `bool?` | `false` creates it stopped. `null` takes the server default |

### Field types

An `IndexField` is a name, a selector path, and a type.

| `IndexFieldType` | Maps to |
|---|---|
| `String` | `string` |
| `Double` | `double` |
| `Int32` | `int` |
| `Int64` | `long` |

Selector paths are dot-separated and resolve against the decoded JSON payload. `data.customerId`
reads a root member; `data.shipping.country` walks two levels in.

## Read from an index

A populated index exposes itself as a stream named `$idx-user-{name}`. Read it with
`RecordFilter.ByUserIndex`:

```csharp
var all = new ReadRecordsOptions {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.ByUserIndex("ORDERS_BY_CUSTOMER")
};
```

To narrow further on the indexed columns, name the index stream in an expression:

```csharp
var highValue = new ReadRecordsOptions {
    Start  = LogPosition.Earliest,
    Filter = RecordFilter.ByExpression(
        "stream = '$idx-user-ORDERS_BY_CUSTOMER' AND data.amount > 1000")
};
```

## Inspect

```csharp
IReadOnlyList<Index> indexes = await client.Indexes.List();
Index orders                 = await client.Indexes.Get("ORDERS_BY_CUSTOMER");
```

**Returns `Index`:**

| Member | Type | Meaning |
|---|---|---|
| `Name` | `IndexName` | The name |
| `Filter` | `string` | The original filter expression |
| `Fields` | `IReadOnlyList<IndexField>` | The indexed columns |
| `State` | `IndexState` | `Unspecified`, `Started`, `Stopped`, `Deleted` |

## Start and stop

Stopping pauses ingestion. The index keeps what it has already built, and starting resumes from
there.

```csharp
await client.Indexes.Stop("ORDERS_BY_CUSTOMER");
await client.Indexes.Start("ORDERS_BY_CUSTOMER");
```

## Delete an index

Permanent. The index and everything it built are gone, and rebuilding means re-indexing the log from
scratch.

```csharp
await client.Indexes.Delete("ORDERS_BY_CUSTOMER");
```
