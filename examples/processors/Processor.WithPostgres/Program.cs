using Dapper;
using Kurrent.Client.Streams;
using Kurrent.Processors;
using Npgsql;
using Processor.WithPostgres;
using Serilog;
using Transfermarkt.Domain;

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
    builder.Services.AddKurrentProcessor<ClubSpendingProcessor>(opts => {
        opts.Connection("kurrentdb://localhost:2113");
        opts.Subscription.Filter = RecordFilter.ByExpression($"schema = {nameof(PlayerTransferred)}");
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
