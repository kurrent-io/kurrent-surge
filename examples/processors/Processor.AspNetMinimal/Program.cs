using System;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Processor.AspNetMinimal;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    builder.Services.AddKurrent(kurrent => {
        kurrent.Connection("kurrentdb://admin:changeit@127.0.0.1:32769?tls=false");

        kurrent.AddProcessor(new LiveTransferProcessor(), prx => {
            prx.ProcessorId                  = $"live-transfer-processor-{Guid.NewGuid().ToString()[..8]}";
            prx.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
            prx.Subscription.Filter          = RecordFilter.ByExpression("stream like 'player-%'");

            prx.Checkpoint.Interval = TimeSpan.FromDays(1);
        });
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
