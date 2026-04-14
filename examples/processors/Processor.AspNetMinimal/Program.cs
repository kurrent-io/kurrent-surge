using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Processor.AspNetMinimal;
using Serilog;
using static Kurrent.Processors.Checkpointing.Contracts;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddKurrentProcessor<TransferProcessor>(prx => prx
        .UseClient("kurrentdb://admin:changeit@localhost:2113?tls=false")
        .WithProcessorId($"transfer-processor-{Guid.NewGuid().ToString()[..8]}")
        .ConfigureClient(options => {
            options.Schema.Mapper.Map<PartitionsCheckpointed>("Kurrent.Processors.Checkpointing.PartitionsCheckpointed");
            return options;
        })
        .ConfigureSubscription(x => x.InitialPosition(SubscriptionInitialPosition.Latest)
                                   .FilterExpression("stream like 'player-%'"))
        // .WithFilter("schema_name IN ('PlayerTransferred', 'PlayerRegistered')")
        // .WithFilter("stream like 'player-%'")

        .Checkpointing(x => x.Interval(TimeSpan.FromDays(1))));

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
