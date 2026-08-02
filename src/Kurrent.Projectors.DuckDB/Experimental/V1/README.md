# Projectors V1

Transactional projections for DuckDB: the resume position lives in the projected database and commits in
the same transaction as the rows it describes, so the two can never disagree after a crash.

A projection is an ordinary `IRecordBatchProcessor`. It differs from one only in claiming its checkpoints
during `OnInitialize`, which makes it — not the drone's store — the authority on where the subscription
resumes. See [DESIGN.md](DESIGN.md) for how and why.

## Layout

```
Experimental/V1/
  Abstractions/  the generic framework. No DuckDB, no Quack — Kurrent.Surge types only.
  DuckDB/        the DuckDB backend: DuckDB.NET.Data only, zero Kurrent.Quack references.
  Quack/         opt-in backend for Quack's prepared-statement / AOT path.
  Examples/      worked examples of every shape, DuckDB and Quack side by side.
  DESIGN.md      how it works, and the hazards.
```

`Abstractions/` is generic over a backend's client type (`TClient`) and is destined to move to
`Kurrent.Surge.Core/Projectors` once proven. Nothing under it may reference `DuckDBConnection`,
`DuckDBAdvancedConnection`, or any other backend type; a backend closes `TClient` instead.

> **Namespace note:** the DuckDB backend's namespace is the flat `Kurrent.Projectors.DuckDB.Experimental.V1`
> — deliberately not `...V1.DuckDB`. A sibling namespace segment shadows `using` directives at enclosing
> scopes before they are even considered, the same collision documented below between `Kurrent.Surge`'s
> `Kurrent.Appender` namespace and `Kurrent.Quack.Appender`. A `...V1.DuckDB` segment would break
> `DuckDB.NET.Data` references the same way.

## The two contracts

```csharp
/// Owns one projection's connection and transaction lifetime, across as many batches as run
/// between checkpoint barriers.
public interface IProjectionTransaction<out TClient> {
    TClient Begin();     // idempotent while a transaction is open
    void    Commit();
    void    Rollback();
}

/// Reads and writes the resume positions in the projection's own storage. Stateless — the client
/// is passed per call, so it never holds a connection or a transaction.
public interface IProjectionCheckpointStore<in TClient> {
    void EnsureStoreExists(TClient client);
    RecordPosition[] Load(TClient client, Partition[] partitions, SubscriptionInitialPosition initial);
    void Store(TClient client, RecordPosition[] expected, RecordPosition[] next);   // guarded CAS
    void Reset(TClient client, LogPosition position, Partition[] partitions);
}
```

`ProjectionBase<TClient>` composes the two and drives the lifecycle. A backend supplies one small class
for each; the projector is the only place they get wired together.

## The four shapes

| shape | when |
|---|---|
| `Projection<TClient>` | full control. Handlers get the client and do whatever they need |
| `DocumentProjection<TClient, TDocument, TKey>` | loads and coalesces per key per batch — one load and one write per key no matter how many messages touch it |
| `UpsertProjection<TClient, TDocument>` | no key, no load. Per-message save; `null` skips |
| `AppendProjection<TDocument>` | DuckDB-only, insert-only, single table via the appender |

## Writing one

```csharp
public class OrdersProjection : DuckDBProjection {
    protected override void Setup(DuckDBConnection connection) {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS orders (id UUID PRIMARY KEY, status VARCHAR)";
        command.ExecuteNonQuery();
    }

    public OrdersProjection() {
        Project<OrderPlaced>((placed, connection, context) => { /* INSERT */ });
        Project<OrderShipped>((shipped, connection, context) => { /* UPDATE */ });
    }
}
```

```csharp
var processor = DuckDBProjector.Create(projection, opts => {
    opts.ProcessorId        = "orders";
    opts.ConnectionProvider = new DuckDBConnectionStringProvider("Data Source=orders.db");
    opts.Connection("kurrentdb://localhost:2113?tls=false");
});

await processor.RunAsync(cancellationToken);
```

Handlers must never open a transaction — one is already open on the client they are given.

## Read this before changing anything

`DESIGN.md` documents four hazards that are silent when you get them wrong: appender lifetime, calling
`ctx.Track` from a checkpoint owner, what `Reset` does and does not clear, and ancestor-namespace
binding. Each one fails without an error message.
