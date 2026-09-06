using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Processor.OrderEnrichment;
using Serilog;

// Order Enrichment processor - stream-table join pattern
// Builds lookup tables from reference data and enriches orders on-the-fly
// Seeds its own reference data and orders. Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog();

    builder.Services.AddSingleton<OrderEnrichmentProcessor>();

    builder.Services.AddKurrentSurge(surge => {
        surge.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false");
        // The three source streams by prefix. A stream constraint travels to the server as a regex over
        // $all, so the standard projections' link events ($ce-*, $et-*) never reach the processor.
        surge.AddProcessor<OrderEnrichmentProcessor>(opts =>
            opts.Subscription.Filter = RecordFilter.ByExpression("stream LIKE 'customer-%' OR stream LIKE 'product-%' OR stream LIKE 'order-%'"));
    });

    builder.Services.AddHostedService<OrderSeeder>();

    using var host = builder.Build();

    await host.RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally {
    await Log.CloseAndFlushAsync();
}
