using Kurrent.Surge;
using Kurrent.Surge.Processors.Models;
using Processor.AspNetMinimal;
using Transfermarkt.Seeding;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddSingleton<LiveTransferProcessor>();

    builder.AddKurrentSurge("kurrentdb", surge => {
        surge.AddProcessor<LiveTransferProcessor>(prx => {
            prx.ProcessorId                  = $"live-transfer-processor-{Guid.NewGuid().ToString()[..8]}";
            prx.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
            prx.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");

            // Tailing live data only, so there is nothing to resume after a restart — checkpoint
            // rarely rather than never, so the drone never wastes writes catching up to itself.
            prx.Checkpoint.Interval = TimeSpan.FromDays(1);
        });
    });

    builder.Services.AddHostedService<TransferSeeder>();

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok("Healthy"));

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}
