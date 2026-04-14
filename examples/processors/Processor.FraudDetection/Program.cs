using Kurrent.Client.Streams;
using Processor.FraudDetection;
using Serilog;

// Fraud Detection processor - pattern matching on transaction sequences
// Detects card testing pattern: small tx followed by large tx within 1 minute
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var processor = new FraudDetectionProcessor();

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema like 'Transaction'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
