using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Processor.Minimal;
using Serilog;

// Minimal processor example - the simplest possible setup
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
var processor = new MinimalProcessor(loggerFactory.CreateLogger<MinimalProcessor>());

await KurrentProcessors.Create(processor)
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
