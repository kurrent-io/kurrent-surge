using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.IoTMonitoring;
using Serilog;

// IoT Temperature Monitoring processor
// Monitors sensor readings and raises alerts for threshold violations and anomalies
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = new ExpressionFilter("schema_name = 'SensorReading'");
    opts.Subscribe.Use(new TemperatureMonitorProcessor());
}).RunWithGracefulShutdownAsync();
