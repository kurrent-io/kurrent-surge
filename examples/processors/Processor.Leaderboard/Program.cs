using System.Text.Json;
using Kurrent.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming;
using Processor.Leaderboard;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Configure Redis
    var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var redis = await ConnectionMultiplexer.ConnectAsync(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

    // Register processor as hosted service
    builder.Services.AddSingleton<LeaderboardProcessor>();
    builder.Services.AddKurrentProcessor<LeaderboardProcessor>(opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.Subscription.Filter = RecordFilter.ByExpression("schema_name IN ('ScoreSubmitted', 'PlayerJoined')");
    });

    var app = builder.Build();

    // Health check
    app.MapGet("/health", () => Results.Ok("Healthy"));

    // Get top N players for a game
    app.MapGet("/leaderboard/{gameId}/top/{count:int}", async (string gameId, int count, IConnectionMultiplexer redis) => {
        var db = redis.GetDatabase();
        var leaderboardKey = $"leaderboard:{gameId}";

        // Get top N players with scores (descending order)
        var entries = await db.SortedSetRangeByRankWithScoresAsync(
            leaderboardKey,
            0,
            count - 1,
            Order.Descending
        );

        var results = new List<object>();
        var rank = 1;

        foreach (var entry in entries) {
            var playerId = entry.Element.ToString();
            var playerKey = $"player:{playerId}";
            var playerData = await db.StringGetAsync(playerKey);

            var displayName = playerId;
            var country = "??";

            if (playerData.HasValue) {
                var player = JsonSerializer.Deserialize<PlayerInfo>(playerData.ToString());
                displayName = player?.DisplayName ?? playerId;
                country = player?.Country ?? "??";
            }

            results.Add(new {
                Rank = rank++,
                PlayerId = playerId,
                DisplayName = displayName,
                Country = country,
                Score = (long)entry.Score
            });
        }

        return Results.Ok(new {
            GameId = gameId,
            TotalPlayers = await db.SortedSetLengthAsync(leaderboardKey),
            TopPlayers = results
        });
    });

    // Get a specific player's rank and score
    app.MapGet("/leaderboard/{gameId}/rank/{playerId}", async (string gameId, string playerId, IConnectionMultiplexer redis) => {
        var db = redis.GetDatabase();
        var leaderboardKey = $"leaderboard:{gameId}";

        var score = await db.SortedSetScoreAsync(leaderboardKey, playerId);
        if (!score.HasValue) {
            return Results.NotFound(new { Message = "Player not found on leaderboard" });
        }

        var rank = await db.SortedSetRankAsync(leaderboardKey, playerId, Order.Descending);
        var totalPlayers = await db.SortedSetLengthAsync(leaderboardKey);

        // Get player info
        var playerKey = $"player:{playerId}";
        var playerData = await db.StringGetAsync(playerKey);
        var displayName = playerId;

        if (playerData.HasValue) {
            var player = JsonSerializer.Deserialize<PlayerInfo>(playerData.ToString());
            displayName = player?.DisplayName ?? playerId;
        }

        return Results.Ok(new {
            PlayerId     = playerId,
            DisplayName  = displayName,
            Score        = (long)score.Value,
            Rank         = (rank ?? 0) + 1,
            TotalPlayers = totalPlayers,
            Percentile   = Math.Round((double)(totalPlayers - rank - 1)! / totalPlayers * 100, 1)
        });
    });

    // Get player info
    app.MapGet("/player/{playerId}", async (string playerId, IConnectionMultiplexer redis) => {
        var db   = redis.GetDatabase();
        var key  = $"player:{playerId}";
        var data = await db.StringGetAsync(key);

        return data.HasValue
            ? Results.Content(data!, "application/json")
            : Results.NotFound();
    });

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}

record PlayerInfo(string PlayerId, string DisplayName, string Country, string JoinedAt);
