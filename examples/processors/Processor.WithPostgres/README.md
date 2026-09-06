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
public async ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
    var toFlush = _pending.ToArray();
    _pending.Clear();

    await using var conn = await dataSource.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    foreach (var (_, spending) in toFlush) {
        await conn.ExecuteAsync("""
            INSERT INTO club_spending (club_id, season, total_spent, total_received)
            VALUES (@ClubId, @Season, @TotalSpent, @TotalReceived)
            ON CONFLICT (club_id, season) DO UPDATE SET
                total_spent = club_spending.total_spent + EXCLUDED.total_spent,
                total_received = club_spending.total_received + EXCLUDED.total_received
            """, spending, tx);
    }

    await tx.CommitAsync();
}
```

### Program.cs

Registers `NpgsqlDataSource` via DI:

```csharp
builder.Services.AddNpgsqlDataSource(connectionString);
builder.Services.AddSingleton<ClubSpendingProcessor>();
builder.Services.AddHostedService<ClubSpendingService>();
```

## Endpoints

- `GET /health` - Health check
- `GET /spending/{clubId}/{season}` - Query club spending for a season

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=transfermarkt;Username=postgres;Password=postgres"
  }
}
```

### Environment Variables

```bash
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
