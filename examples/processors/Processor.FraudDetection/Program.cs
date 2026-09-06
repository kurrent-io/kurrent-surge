using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Processor.FraudDetection;
using Serilog;

// Fraud Detection processor - pattern matching on transaction sequences
// Detects card testing pattern: small tx followed by large tx within 1 minute
// Seeds its own Transaction events. Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog();

    builder.Services.AddSingleton<FraudDetectionProcessor>();

    builder.Services.AddKurrentSurge(surge => {
        surge.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false");
        surge.AddProcessor<FraudDetectionProcessor>(opts =>
            opts.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'Transaction'"));
    });

    builder.Services.AddHostedService<TransactionSeeder>();

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
