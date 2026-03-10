using Kurrent.Client.Streams;
using Processor.OrderEnrichment;
using Serilog;

// Order Enrichment processor - stream-table join pattern
// Builds lookup tables from reference data and enriches orders on-the-fly
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var processor = new OrderEnrichmentProcessor();

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name IN ('OrderPlaced', 'CustomerUpdated', 'ProductUpdated')")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
