# Processor.WithRedisCache

Demonstrates using Redis as a cache for fast player lookups and enrichment scenarios.

## What You'll Learn

- Using Redis for caching processor state
- Cache-on-write pattern (update cache as events arrive)
- Using `StackExchange.Redis` with dependency injection
- Lookup enrichment pattern for downstream consumers
- TTL-based cache expiration

## Prerequisites

- KurrentDB running locally on port 2113
- Redis running locally on port 6379
- .NET 10 SDK

### Start Redis with Docker

```bash
docker run -d --name redis -p 6379:6379 redis:7
```

## Running the Example

```bash
dotnet run
```

A hosted `TransferSeeder` from the shared `Transfermarkt.Domain` project registers and transfers a
player every second, so `/players` fills up immediately.

## Key Code

### PlayerCacheProcessor.cs

Caches player data on write:

```csharp
async ValueTask CachePlayerInfo(PlayerRegistered registered, CancellationToken ct) {
    var playerData = new CachedPlayer {
        PlayerId    = registered.PlayerId.ToString(),
        Name        = registered.Name.ToString(),
        Nationality = registered.Nationality.ToString(),
        Position    = registered.MainPosition.ToString(),
        CurrentClub = null
    };

    var key  = $"player:{registered.PlayerId}";
    var json = JsonSerializer.Serialize(playerData);

    await Db.StringSetAsync(key, json, TimeSpan.FromHours(24));
}
```

Updates club on transfer:

```csharp
async ValueTask UpdatePlayerClub(PlayerTransferred transfer, CancellationToken ct) {
    var key = $"player:{transfer.PlayerId}";
    var existing = await Db.StringGetAsync(key);

    // Update or create cache entry
    var playerData = existing.HasValue
        ? JsonSerializer.Deserialize<CachedPlayer>(existing!) with { CurrentClub = transfer.ToClubId.ToString() }
        : new CachedPlayer { PlayerId = transfer.PlayerId.ToString(), CurrentClub = transfer.ToClubId.ToString() };

    await Db.StringSetAsync(key, JsonSerializer.Serialize(playerData), TimeSpan.FromHours(24));
}
```

### Program.cs

```csharp
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
builder.Services.AddSingleton<PlayerCacheProcessor>();
builder.Services.AddHostedService<PlayerCacheService>();
```

## Endpoints

- `GET /health` - Health check
- `GET /player/{playerId}` - Get cached player data
- `GET /players` - List all cached player keys (max 100)

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

### Environment Variables

```bash
export ConnectionStrings__Redis="localhost:6379"
```

## Use Cases

### Enrichment Pattern

Other services can query the Redis cache to enrich their data:

```csharp
// In another service
var playerJson = await redis.GetDatabase().StringGetAsync($"player:{playerId}");
var player = JsonSerializer.Deserialize<CachedPlayer>(playerJson);
// Use player.Name, player.Nationality, etc. to enrich your data
```

### Real-time Lookups

Fast lookups for UI or API responses without hitting the event store.

## Next Steps

- Combine with Postgres persistence (see `Processor.FullExample`)
- Add pub/sub for cache invalidation
- Use Redis Streams for additional event processing
