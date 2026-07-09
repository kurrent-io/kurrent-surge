using Dapper;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Streaming;
using Npgsql;
using Processor.WithPostgres;
using Serilog;

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
    builder.Services.AddKurrent(kurrent => {
        kurrent.Connection("kurrentdb://localhost:2113");
        kurrent.AddProcessor<ClubSpendingProcessor>(opts =>
            opts.Subscription.Filter = RecordFilter.ByExpression("schema = 'PlayerTransferred'"));
    });

    var app = builder.Build();

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
