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

    builder.AddKurrentSurge(surge => {
        surge.FailProcessOnFatalError();

        surge.AddProcessor<FraudDetectionProcessor>(options =>
            options.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'Transaction'"));
    });

    builder.Services.AddSerilog();

    builder.Services.AddHostedService<TransactionSeeder>();

    await builder.Build().RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally {
    await Log.CloseAndFlushAsync();
}
