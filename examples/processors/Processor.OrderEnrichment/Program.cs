using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.OrderEnrichment;
using Serilog;

// Order Enrichment processor - stream-table join pattern
// Builds lookup tables from reference data and enriches orders on-the-fly
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = new ExpressionFilter("schema_name IN ('OrderPlaced', 'CustomerUpdated', 'ProductUpdated')");
    opts.Subscribe.Use(new OrderEnrichmentProcessor());
}).RunWithGracefulShutdownAsync();
