using Kurrent.Client;
using Kurrent.Processors.Options;
using Npgsql;
using Processor.FullExample;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Processor.FullExample", LogEventLevel.Debug)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try {
    Log.Information("Starting Transfer Analytics application");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Configure Postgres
    var pgConn = builder.Configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Database=transfermarkt;Username=postgres;Password=postgres";
    builder.Services.AddNpgsqlDataSource(pgConn);

    // Configure Redis
    var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var redis = await ConnectionMultiplexer.ConnectAsync(redisConn);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

    // Register shared Kurrent client
    builder.Services.AddKurrentClient("kurrentdb://localhost:2113");

    // Register processor as hosted service
    builder.Services.AddKurrentProcessor<TransferAnalyticsProcessor>(config => config
        .WithFilter("schema_name IN ('PlayerRegistered', 'PlayerTransferred')")
        // .WithMaxParallelism(2)
        .ConfigureCheckpointing(c => c.Interval(10_000))
        .ConfigureBatching(b => b.MaxSize(50).Timeout(1000))
    );

    // Health checks
    builder.Services.AddHealthChecks()
        .AddNpgSql(pgConn, name: "postgres")
        .AddRedis(redisConn, name: "redis");

    var app = builder.Build();

    // Health check endpoint
    app.MapHealthChecks("/health");

    // API endpoints
    app.MapGet("/player/{playerId}", async (string playerId, IConnectionMultiplexer redis) => {
        var data = await redis.GetDatabase().StringGetAsync($"player:{playerId}");
        return data.HasValue
            ? Results.Content(data!, "application/json")
            : Results.NotFound();
    });

    app.MapGet("/spending/{clubId}/{season:int}", async (string clubId, int season, NpgsqlDataSource db) => {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM club_spending WHERE club_id = $1 AND season = $2";
        cmd.Parameters.AddWithValue(clubId);
        cmd.Parameters.AddWithValue(season);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.NotFound();

        var result = new {
            club_id        = reader.GetString(0),
            season         = reader.GetInt32(1),
            total_spent    = reader.GetDecimal(2),
            total_received = reader.GetDecimal(3),
            players_in     = reader.GetInt32(4),
            players_out    = reader.GetInt32(5),
            updated_at     = reader.GetDateTime(6)
        };

        return Results.Ok(result);
    });

    app.MapGet("/spending/top/{season:int}", async (int season, NpgsqlDataSource db) => {
        await using var conn = await db.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT club_id, season, total_spent, total_received,
                   (total_spent - total_received) as net_spend,
                   players_in, players_out
            FROM club_spending
            WHERE season = $1
            ORDER BY (total_spent - total_received) DESC
            LIMIT 10
            """;
        cmd.Parameters.AddWithValue(season);

        var results = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            results.Add(new {
                club_id        = reader.GetString(0),
                season         = reader.GetInt32(1),
                total_spent    = reader.GetDecimal(2),
                total_received = reader.GetDecimal(3),
                net_spend      = reader.GetDecimal(4),
                players_in     = reader.GetInt32(5),
                players_out    = reader.GetInt32(6)
            });
        }

        return Results.Ok(results);
    });

    Log.Information("Application configured, starting web host");
    await app.RunAsync();







    // var client = KurrentClient.Create("kurrentdb://localhost:2113");
    //
    // var requests = new List<AppendStreamRequest> {
    //     AppendStreamRequest.New().ForStream("Players-001")
    //         .ExpectingRevision(ExpectedStreamState.NoStream)
    //         .WithRecord(PlayerRegistered.Create("001", "Cantona", Country.France, Position.CentreForward))
    //         .Build(),
    //     AppendStreamRequest.New().ForStream("Players-001")
    //         // all would work the same on the server side for the intended purpose
    //         // the developer needs to do nothing but if it does, they would all work
    //         // the append session method would just pass any cause it doesnt matter.
    //         // .ExpectingRevision(0)
    //         // .ExpectingRevision(ExpectedStreamState.Any)
    //         // .ExpectingRevision(ExpectedStreamState.StreamExists)
    //         .WithRecord(PlayerRetired.Create("001"))
    //         .Build()
    // }; // this will be a builder in real code
    //
    // var result = await client.Streams.AppendSessionAsync(requests);
    //
    //
    // // Any subsequent appends to the same Stream implies it exists and has a revision.
    // // so what is there to rationalize?
    // // I really want to understand the "edge" case I'm missing
    // // I want to throw if there is any ambiguity in the expectations
    // //
    // //// all would work the same on the server side for the intended purpose
    // // // the developer needs to do nothing but if it does, they would all work
    // // // the append session method would just pass any cause it doesnt matter.
    // // // .ExpectingRevision(0)
    // // // .ExpectingRevision(ExpectedStreamState.Any)
    // // // .ExpectingRevision(ExpectedStreamState.StreamExists)
    //
    // ValueTask<StreamRevision> AppendRecords(StreamName stream, IEnumerable<AppendRecord> records) =>
    //     throw new NotImplementedException();
    //
    // var expectations = new Dictionary<StreamName, ExpectedStreamState>(requests.Count);
    //
    // foreach (var (stream, expectedState, records) in requests) {
    //     if (expectations.TryGetValue(stream, out var tracked)) {
    //         // We've seen this stream before - validate compatibility
    //         var isCompatible = expectedState == tracked
    //                         || expectedState == ExpectedStreamState.Any
    //                         || expectedState == ExpectedStreamState.StreamExists;
    //
    //         if (!isCompatible)
    //             throw new InvalidOperationException(
    //                 $"Conflicting expectation for '{stream}': session expects {tracked}, but request specifies {expectedState}");
    //     }
    //
    //     // Append and track actual revision after written to the stream.
    //     expectations[stream] = await AppendRecords(stream, records);
    // }
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally {
    await Log.CloseAndFlushAsync();
}
