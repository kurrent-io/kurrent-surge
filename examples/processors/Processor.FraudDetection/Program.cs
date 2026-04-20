using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.FraudDetection;
using Serilog;

// Fraud Detection processor - pattern matching on transaction sequences
// Detects card testing pattern: small tx followed by large tx within 1 minute
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(new FraudDetectionProcessor(), opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = RecordFilter.ByExpression("schema like 'Transaction'");
}).RunWithGracefulShutdownAsync();
