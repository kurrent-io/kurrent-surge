using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.WordCount;
using Serilog;

// Word Count processor - classic stream processing example
// Counts word frequencies across all incoming text events
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = new ExpressionFilter("schema_name = 'TextSubmitted'");
    opts.Subscribe.Use(new WordCountProcessor());
}).RunWithGracefulShutdownAsync();
