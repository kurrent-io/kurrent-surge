using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Processor.ClickstreamSessions;
using Serilog;

// Clickstream Session Analytics processor
// Groups page views into user sessions based on 30-minute inactivity timeout
// Seeds its own PageView events. Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog();

    builder.Services.AddSingleton<SessionAnalyticsProcessor>();

    builder.Services.AddKurrentSurge(surge => {
        surge.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false");
        surge.AddProcessor<SessionAnalyticsProcessor>(opts =>
            opts.Subscription.Filter = RecordFilter.ByExpression("schema = 'PageView'"));
    });

    builder.Services.AddHostedService<PageViewSeeder>();

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
