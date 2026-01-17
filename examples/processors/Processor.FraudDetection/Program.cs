using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Processor.FraudDetection;
using Serilog;

// Fraud Detection processor - pattern matching on transaction sequences
// Detects card testing pattern: small tx followed by large tx within 1 minute
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
var processor = new FraudDetectionProcessor(loggerFactory.CreateLogger<FraudDetectionProcessor>());

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name = 'Transaction'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
