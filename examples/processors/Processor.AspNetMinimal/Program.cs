using Kurrent.Streaming.Models;
using Processor.AspNetMinimal;
using static Kurrent.Streaming.Checkpointing.Contracts;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddSingleton<TransferProcessor>();
    builder.Services.AddKurrentProcessor<TransferProcessor>(prx => {
        prx.Connection(
            "kurrentdb://admin:changeit@localhost:2113?tls=false",
            opts => opts.Schema.Mapper
                .Map<PartitionsCheckpointed>("Kurrent.Streaming.Checkpointing.PartitionsCheckpointed"));
        
        prx.ProcessorId                  = $"transfer-processor-{Guid.NewGuid().ToString()[..8]}";
        prx.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
        prx.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
        
        prx.Checkpoint.Interval = TimeSpan.FromDays(1);
    });

    var app = builder.Build();

    app.MapGet("/health", () => Results.Ok("Healthy"));

    SeedData.StartSeeding();

    await app.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally {
    await Log.CloseAndFlushAsync();
}
