using Kurrent.Client.Streams;
using Processor.IoTMonitoring;
using Serilog;

// IoT Temperature Monitoring processor
// Monitors sensor readings and raises alerts for threshold violations and anomalies
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var processor = new TemperatureMonitorProcessor();

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name = 'SensorReading'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
