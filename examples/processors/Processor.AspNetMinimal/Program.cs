using Kurrent.Client.Streams;
using Kurrent.Processors;
using Kurrent.Processors.Models;
using Processor.AspNetMinimal;
using Serilog;
using static Kurrent.Processors.Checkpointing.Contracts;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddSingleton<TransferProcessor>();
    builder.Services.AddKurrentProcessor<TransferProcessor>(opts => {
        opts.Connection(
            "kurrentdb://admin:changeit@localhost:2113?tls=false",
            options => {
                options.Schema.Mapper.Map<PartitionsCheckpointed>("Kurrent.Processors.Checkpointing.PartitionsCheckpointed");
                return options;
            });
        opts.ProcessorId                  = $"transfer-processor-{Guid.NewGuid().ToString()[..8]}";
        opts.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
        opts.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");
        opts.Checkpointing(x => x.Interval(TimeSpan.FromDays(1)));
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
