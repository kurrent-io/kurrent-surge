# Kurrent.Projectors.DuckDB

A [DuckDB](https://duckdb.org) projection target for [KurrentDB](https://kurrent.io) —
project record streams into DuckDB tables with automatic checkpointing.

## Installation

```shell
dotnet add package Kurrent.Projectors.DuckDB
```

### Supported Frameworks

- .NET 10.0

## Quick Start

```csharp
using Kurrent.Projectors.DuckDB;

public class OrdersProjection : DuckDBProjection {
    public OrdersProjection() {
        Setup(async (provider, ct) => {
            using var db = provider.GetConnection();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS orders (id TEXT, total DECIMAL)";
            await cmd.ExecuteNonQueryAsync(ct);
        });

        Project<OrderPlaced>(async (order, provider, ctx) => {
            using var db = provider.GetConnection();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO orders VALUES ($id, $total)";
            // bind and execute...
        });
    }
}

await KurrentProjector.Create(new OrdersProjection(), opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.ProcessorId   = "orders-projection";
    opts.ResolveClient = _ => ValueTask.FromResult(connectionProvider);
}).RunAsync(cancellationToken);
```

## Features

- **DuckDB Projections** — strongly-typed projection base class with setup and per-message handlers
- **Connection Management** — pooled `IDuckDBConnectionProvider` for file-backed or in-memory databases
- **Checkpoint Store** — durable projection positions persisted in DuckDB
- **Automatic Checkpointing** — inherits the Kurrent.Streaming processor runtime
- **OpenTelemetry** — distributed tracing and metrics for projection operations

## Links

- [Documentation](https://kurrent.io/docs)
- [GitHub Repository](https://github.com/kurrent-io/Kurrent.Client)
- [License (Apache 2.0)](https://github.com/kurrent-io/Kurrent.Client/blob/main/LICENSE.md)
