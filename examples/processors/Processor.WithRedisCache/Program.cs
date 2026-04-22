using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.WithRedisCache;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
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
    builder.Services.AddSingleton<PlayerCacheProcessor>();
    builder.Services.AddKurrentProcessor<PlayerCacheProcessor>(opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.Subscription.Filter = RecordFilter.ByExpression("schema_name IN ('PlayerRegistered', 'PlayerTransferred')");
    });

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok("Healthy"));

    // Query endpoint to lookup cached player
    app.MapGet("/player/{playerId}", static async (string playerId, IConnectionMultiplexer redis) => {
        var db  = redis.GetDatabase();
        var key = $"player:{playerId}";
        var data = await db.StringGetAsync(key);

        return data.HasValue
            ? Results.Content(data!, "application/json")
            : Results.NotFound();
    });

    // List all cached player keys
    app.MapGet("/players", async (IConnectionMultiplexer redis) => {
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: "player:*").Take(100).Select(k => k.ToString()).ToList();
        return Results.Ok(keys);
    });

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}
