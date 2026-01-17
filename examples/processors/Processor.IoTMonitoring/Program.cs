using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Processor.IoTMonitoring;
using Serilog;

// IoT Temperature Monitoring processor
// Monitors sensor readings and raises alerts for threshold violations and anomalies
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
var processor = new TemperatureMonitorProcessor(loggerFactory.CreateLogger<TemperatureMonitorProcessor>());

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name = 'SensorReading'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
