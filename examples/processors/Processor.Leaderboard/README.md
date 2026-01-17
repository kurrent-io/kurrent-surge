# Processor.Leaderboard

Real-time gaming leaderboard using Redis sorted sets with REST API endpoints.

## What You'll Learn

- Using Redis sorted sets for leaderboards
- External state management with Redis
- ASP.NET integration with Kurrent Processors
- Building query APIs over processed data

## How It Works

1. **Input**: Two event types:
   - `PlayerJoined` → Store player profile in Redis hash
   - `ScoreSubmitted` → Update Redis sorted set (only if new high score)
2. **State**: Redis sorted sets provide O(log N) rank queries
3. **Output**: REST API for leaderboard queries

```
PlayerJoined(player1, "xX_ProGamer_Xx", "US")
    → Redis: player:player1 = {displayName: "xX_ProGamer_Xx", ...}

ScoreSubmitted(main, player1, 15000)
    → Redis: ZADD leaderboard:main 15000 player1
    → 🏆 NEW HIGH SCORE | Player: xX_ProGamer_Xx | Score: 15,000 | Rank: #42
```

## Prerequisites

- KurrentDB running on `localhost:2113`
- Redis running on `localhost:6379`

## Running the Example

```bash
# Start Redis (if not running)
docker run -d -p 6379:6379 redis:alpine

cd examples/processors/Processor.Leaderboard
dotnet run
```

## API Endpoints

### Get Top N Players
```bash
curl http://localhost:5000/leaderboard/main/top/10
```

Response:
```json
{
  "gameId": "main",
  "totalPlayers": 1523,
  "topPlayers": [
    { "rank": 1, "playerId": "p1", "displayName": "xX_ProGamer_Xx", "country": "US", "score": 98500 },
    { "rank": 2, "playerId": "p2", "displayName": "NoobSlayer99", "country": "UK", "score": 95200 },
    ...
  ]
}
```

### Get Player Rank
```bash
curl http://localhost:5000/leaderboard/main/rank/player123
```

Response:
```json
{
  "playerId": "player123",
  "displayName": "CasualGamer",
  "score": 45000,
  "rank": 156,
  "totalPlayers": 1523,
  "percentile": 89.8
}
```

### Get Player Info
```bash
curl http://localhost:5000/player/player123
```

## Key Code

```csharp
async ValueTask ProcessScore(ScoreSubmitted score) {
    var leaderboardKey = $"leaderboard:{score.GameId}";
    var currentBest = await Db.SortedSetScoreAsync(leaderboardKey, score.PlayerId);

    // Only update if new personal best
    if (!currentBest.HasValue || score.Score > currentBest.Value) {
        await Db.SortedSetAddAsync(leaderboardKey, score.PlayerId, score.Score);
        var rank = await Db.SortedSetRankAsync(leaderboardKey, score.PlayerId, Order.Descending);
        // ...
    }
}
```

## Redis Data Structures

| Key Pattern | Type | Description |
|-------------|------|-------------|
| `player:{id}` | String (JSON) | Player profile info |
| `leaderboard:{gameId}` | Sorted Set | Score → Player mapping |

## Sample Output

```
[INF] 🎮 PLAYER JOINED | p-001: xX_ProGamer_Xx (US)
[INF] 🏆 NEW HIGH SCORE | Game: main | Player: xX_ProGamer_Xx | Score: 15,000 | Rank: #42
[INF]    └─ 🌟 xX_ProGamer_Xx is now in the TOP 10!
[INF] Checkpoint #3 | Scores: 1,245 | Players: 89 | Leaderboard size: 89
```

## Original Tutorials

- [RisingWave Gaming Leaderboard](https://risingwave.com/blog/real-time-gaming-leaderboard/)
- [Redpanda Real-Time Leaderboard](https://www.redpanda.com/blog/build-real-time-leaderboard-gaming)
- [Redis Sorted Sets Documentation](https://redis.io/docs/data-types/sorted-sets/)
