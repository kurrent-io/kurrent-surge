using Kurrent.Surge.Client.Streams;
using Kurrent.Streaming;
using Processor.FraudDetection;
using Serilog;
using Serilog.Extensions.Logging;

// Fraud Detection processor - pattern matching on transaction sequences
// Detects card testing pattern: small tx followed by large tx within 1 minute
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(new FraudDetectionProcessor(), opts => {
    opts.Connection("kurrentdb://localhost:2113", client => client.LoggerFactory = new SerilogLoggerFactory());
    opts.Subscription.Filter = RecordFilter.ByExpression("schema like 'Transaction'");
}).RunWithGracefulShutdownAsync();
