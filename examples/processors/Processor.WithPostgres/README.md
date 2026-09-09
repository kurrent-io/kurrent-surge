# Processor.WithPostgres

Demonstrates persisting processor state to PostgreSQL, with aggregates flushed on checkpoint.

## What You'll Learn

- Storing processor state in PostgreSQL
- Using `OnCheckpoint` to batch database writes
- Upsert pattern for aggregation (`ON CONFLICT DO UPDATE`)
- Transaction handling for reliable persistence
- Using `NpgsqlDataSource` with dependency injection

## Prerequisites

- KurrentDB running locally on port 2113
- PostgreSQL running locally on port 5432
- .NET 10 SDK

### Start PostgreSQL with Docker

```bash
docker run -d --name postgres \
  -e POSTGRES_DB=transfermarkt \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  postgres:16
```

## Running the Example

```bash
dotnet run
```

A hosted `TransferSeeder` from the shared `Transfermarkt.Domain` project transfers a player every
second. Aggregates are flushed to Postgres at each checkpoint, every 10 seconds.

## Key Code

### ClubSpendingProcessor.cs

Aggregates transfers in memory, flushes to Postgres on checkpoint:

```csharp
public async ValueTask OnCheckpoint(CheckpointContext context) {
    if (_pending.IsEmpty)
        return;

    var toFlush = _pending.ToArray();
    _pending.Clear();

    await using var conn = await dataSource.OpenConnectionAsync(context.CancellationToken);
    await using var tx = await conn.BeginTransactionAsync(context.CancellationToken);

    try {
        foreach (var (_, spending) in toFlush) {
            await conn.ExecuteAsync("""
                INSERT INTO club_spending (club_id, season, total_spent, total_received, players_in, players_out, updated_at)
                VALUES (@ClubId, @Season, @TotalSpent, @TotalReceived, @PlayersIn, @PlayersOut, NOW())
                ON CONFLICT (club_id, season) DO UPDATE SET
                    total_spent = club_spending.total_spent + EXCLUDED.total_spent,
                    total_received = club_spending.total_received + EXCLUDED.total_received,
                    players_in = club_spending.players_in + EXCLUDED.players_in,
                    players_out = club_spending.players_out + EXCLUDED.players_out,
                    updated_at = NOW()
                """,
                spending,
                tx
            );
        }

        await tx.CommitAsync(context.CancellationToken);
    }
    catch {
        await tx.RollbackAsync(context.CancellationToken);
        throw;
    }
}
```

### Program.cs

Registers `NpgsqlDataSource` via DI, then opens the Kurrent Surge registration scope on the connection string named `kurrentdb`:

```csharp
builder.Services.AddNpgsqlDataSource(connectionString);
builder.Services.AddSingleton<ClubSpendingProcessor>();

builder.AddKurrentSurge("kurrentdb", surge => {
    surge.AddProcessor<ClubSpendingProcessor>(opts => {
        opts.Subscription.Filter = RecordFilter.ByExpression("schema = 'PlayerTransferred'");
        opts.Checkpoint.Interval = TimeSpan.FromSeconds(10);
    });
});

builder.Services.AddHostedService<TransferSeeder>();
```

## Endpoints

- `GET /health` - Health check
- `GET /spending/{clubId}/{season}` - Query club spending for a season

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "kurrentdb": "kurrentdb://admin:changeit@localhost:2113?tls=false",
    "Postgres": "Host=localhost;Database=transfermarkt;Username=postgres;Password=postgres"
  }
}
```

### Environment Variables

```bash
export ConnectionStrings__kurrentdb="kurrentdb://admin:changeit@localhost:2113?tls=false"
export ConnectionStrings__Postgres="Host=localhost;Database=transfermarkt;Username=postgres;Password=postgres"
```

## Database Schema

`Program.cs` creates the following table on startup (`DatabaseSchema.CreateClubSpendingTable`):

```sql
CREATE TABLE club_spending (
    club_id TEXT NOT NULL,
    season INT NOT NULL,
    total_spent DECIMAL(18,2) NOT NULL DEFAULT 0,
    total_received DECIMAL(18,2) NOT NULL DEFAULT 0,
    players_in INT NOT NULL DEFAULT 0,
    players_out INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (club_id, season)
);
```

## Next Steps

- Add caching layer (see `Processor.WithRedisCache`)
- See `Processor.FullExample` for Postgres + Redis + full logging
