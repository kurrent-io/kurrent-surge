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

    builder.Services.AddKurrentSurge(surge => {
        surge.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false");

        surge.AddProcessor(new LiveTransferProcessor(), prx => {
            prx.ProcessorId                  = $"live-transfer-processor-{Guid.NewGuid().ToString()[..8]}";
            prx.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
            prx.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");

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
