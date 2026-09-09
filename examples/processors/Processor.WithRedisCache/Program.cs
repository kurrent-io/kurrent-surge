using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Models;
using Processor.WithRedisCache;
using Serilog;
using StackExchange.Redis;
using Transfermarkt.Seeding;

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

    builder.AddKurrentSurge("kurrentdb", surge => {
        surge.AddProcessor<PlayerCacheProcessor>(opts => {
            opts.ProcessorId = "PlayerCacheProcessor-001";
            opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
            opts.Checkpoint.Interval = TimeSpan.FromSeconds(5);
            opts.Subscription.Filter = RecordFilter.ByExpression("schema_name IN ('PlayerRegistered', 'PlayerTransferred')");
        });
    });

    builder.Services.AddHostedService<TransferSeeder>();

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
    app.MapGet("/players", static async (IConnectionMultiplexer redis) => {
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
