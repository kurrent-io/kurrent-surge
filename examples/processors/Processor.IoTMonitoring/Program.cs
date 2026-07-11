using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming;
using Processor.IoTMonitoring;
using Serilog;

// IoT Temperature Monitoring processor
// Monitors sensor readings and raises alerts for threshold violations and anomalies
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(new TemperatureMonitorProcessor(), opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'SensorReading'");
}).RunWithGracefulShutdownAsync();
