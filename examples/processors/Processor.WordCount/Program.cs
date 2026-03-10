using Kurrent.Client.Streams;
using Processor.WordCount;
using Serilog;

// Word Count processor - classic stream processing example
// Counts word frequencies across all incoming text events
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var processor = new WordCountProcessor();

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name = 'TextSubmitted'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
