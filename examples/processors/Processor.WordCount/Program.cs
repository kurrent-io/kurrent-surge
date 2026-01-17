using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Processor.WordCount;
using Serilog;

// Word Count processor - classic stream processing example
// Counts word frequencies across all incoming text events
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
var processor = new WordCountProcessor(loggerFactory.CreateLogger<WordCountProcessor>());

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name = 'TextSubmitted'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
