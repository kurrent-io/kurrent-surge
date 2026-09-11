using Kurrent.Surge;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Processor.IoTMonitoring;
using Serilog;

// IoT Temperature Monitoring processor
// Monitors sensor readings and raises alerts for threshold violations and anomalies
// Seeds its own SensorReading events. Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try {
    var builder = SurgeProcessor.CreateBuilder<TemperatureMonitorProcessor>(args, options =>
        options.Subscription.Filter = RecordFilter.ByExpression("schema_name = 'SensorReading'"));

    builder.Services.AddSerilog();

    builder.Services.AddHostedService<SensorReadingSeeder>();

    await builder.Build().RunAsync();
}
catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally {
    await Log.CloseAndFlushAsync();
}
