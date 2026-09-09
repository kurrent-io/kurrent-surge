using Dapper;
using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Npgsql;
using Processor.WithPostgres;
using Serilog;
using Transfermarkt.Data;
using Transfermarkt.Seeding;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Configure Postgres
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Database=transfermarkt;Username=postgres;Password=postgres";

    builder.Services.AddNpgsqlDataSource(connectionString);

    // Register processor as hosted service
    builder.Services.AddSingleton<ClubSpendingProcessor>();
    builder.AddKurrentSurge("kurrentdb", surge => {
        surge.AddProcessor<ClubSpendingProcessor>(opts => {
            opts.Subscription.Filter = RecordFilter.ByExpression("schema = 'PlayerTransferred'");
            opts.Checkpoint.Interval = TimeSpan.FromSeconds(10);
        });
    });

    builder.Services.AddHostedService<TransferSeeder>();

    var app = builder.Build();

    await using (var conn = await app.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync())
        await conn.ExecuteAsync(DatabaseSchema.CreateClubSpendingTable);

    app.MapGet("/health", () => Results.Ok("Healthy"));

    // Query endpoint to see aggregated spending
    app.MapGet("/spending/{clubId}/{season:int}", async (string clubId, int season, NpgsqlDataSource db) => {
        await using var conn = await db.OpenConnectionAsync();
        var result = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM club_spending WHERE club_id = @clubId AND season = @season",
            new { clubId, season }
        );
        return result is not null ? Results.Ok(result) : Results.NotFound();
    });

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}
