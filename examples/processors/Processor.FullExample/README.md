# Processor.FullExample

A production-ready processor combining PostgreSQL for state persistence, Redis for caching,
and structured logging with Serilog.

## What You'll Learn

- Combining multiple integrations in a single processor
- Production logging patterns with Serilog
- Health checks for container orchestration
- Query APIs for both cache and database
- Proper transaction handling
- Batch processing with checkpoints

## Prerequisites

- KurrentDB running locally on port 2113
- PostgreSQL running locally on port 5432
- Redis running locally on port 6379
- .NET 10 SDK

### Start Dependencies with Docker

```bash
# PostgreSQL
docker run -d --name postgres \
  -e POSTGRES_DB=transfermarkt \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  postgres:16

# Redis
docker run -d --name redis -p 6379:6379 redis:7
```

Or use Docker Compose:

```yaml
version: '3.8'
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: transfermarkt
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"

  redis:
    image: redis:7
    ports:
      - "6379:6379"
```

## Running the Example

```bash
dotnet run
```

A hosted `TransferSeeder` from the shared `Transfermarkt.Domain` project registers and transfers a
player every second. The `club_spending` table is created on startup; aggregates are flushed at
each checkpoint, every 10 seconds.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                  KurrentDB (Events)                     │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│            TransferAnalyticsProcessor                    │
│                                                          │
│  ProcessRecord:                                          │
│    - PlayerRegistered → Cache in Redis                   │
│    - PlayerTransferred → Update Redis + Accumulate       │
│                                                          │
│  OnCheckpoint:                                           │
│    - Flush accumulated spending → Postgres               │
└─────────────┬──────────────────────────┬────────────────┘
              │                          │
              ▼                          ▼
┌─────────────────────┐    ┌─────────────────────────────┐
│    Redis Cache      │    │     PostgreSQL              │
│                     │    │                             │
│  player:{id}        │    │  club_spending table        │
│  - Name             │    │  - club_id, season          │
│  - Nationality      │    │  - total_spent/received     │
│  - CurrentClub      │    │  - players_in/out           │
└─────────────────────┘    └─────────────────────────────┘
```

## Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check (Postgres + Redis) |
| `GET /player/{playerId}` | Get cached player from Redis |
| `GET /spending/{clubId}/{season}` | Get club spending from Postgres |
| `GET /spending/top/{season}` | Top 10 net spenders for a season |

## Key Code

### TransferAnalyticsProcessor.cs

Handles multiple concerns in one processor:

```csharp
public async ValueTask ProcessRecord(ProcessRecordContext context) {
    switch (context.Record.Value) {
        case PlayerRegistered registered:
            await CachePlayer(registered, context.Logger);   // Redis
            break;

        case PlayerTransferred transfer:
            await UpdatePlayerCache(transfer);                // Redis
            AccumulateSpending(transfer);                     // In-memory
            break;
    }
}

public async ValueTask OnCheckpoint(CheckpointContext context) {
    if (_pendingSpending.IsEmpty)
        return;

    // Flush accumulated spending to Postgres
    var toFlush = _pendingSpending.ToArray();
    _pendingSpending.Clear();

    await using var conn = await postgres.OpenConnectionAsync(context.CancellationToken);
    await using var tx = await conn.BeginTransactionAsync(context.CancellationToken);
    // ... upsert logic
    await tx.CommitAsync(context.CancellationToken);
}
```

### Program.cs

Full production setup:

```csharp
// Dependencies
builder.Services.AddNpgsqlDataSource(pgConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(pgConn, name: "postgres")
    .AddRedis(redisConn, name: "redis");

// Processor + Kurrent Surge registration scope
builder.Services.AddSingleton<TransferAnalyticsProcessor>();
builder.AddKurrentSurge("kurrentdb", surge => {
    surge.AddProcessor<TransferAnalyticsProcessor>(opts => {
        opts.Subscription.Filter = RecordFilter.ByExpression("schema_name IN ('PlayerRegistered', 'PlayerTransferred')");
        opts.Checkpointing(c => c.IntervalFrom(10_000));
    });
});

builder.Services.AddHostedService<TransferSeeder>();
```

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "kurrentdb": "kurrentdb://admin:changeit@localhost:2113?tls=false",
    "Postgres": "Host=localhost;Database=transfermarkt;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Processor.FullExample": "Debug"
    }
  }
}
```

### Environment Variables

```bash
export ConnectionStrings__kurrentdb="kurrentdb://admin:changeit@localhost:2113?tls=false"
export ConnectionStrings__Postgres="Host=localhost;Database=transfermarkt;..."
export ConnectionStrings__Redis="localhost:6379"
```

## Production Considerations

1. **Connection pooling**: Npgsql handles this automatically via `NpgsqlDataSource`
2. **Redis connection**: `ConnectionMultiplexer` is thread-safe and should be a singleton
3. **Checkpoint frequency**: Balance between data loss window and I/O overhead
4. **Error handling**: Transaction rollback on failure, let the checkpoint retry
5. **Health checks**: Required for Kubernetes liveness/readiness probes

## Related Examples

- `Processor.WordCount` - Just ProcessRecord, no integrations
- `Processor.AspNetMinimal` - ASP.NET hosting without external deps
- `Processor.WithPostgres` - Postgres only
- `Processor.WithRedisCache` - Redis only
