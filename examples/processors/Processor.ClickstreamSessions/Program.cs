using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;
using Processor.ClickstreamSessions;
using Serilog;

// Clickstream Session Analytics processor
// Groups page views into user sessions based on 30-minute inactivity timeout
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog());
var processor = new SessionAnalyticsProcessor(loggerFactory.CreateLogger<SessionAnalyticsProcessor>());

await KurrentProcessors.Create(processor)
    .WithFilterExpression("schema_name = 'PageView'")
    .ConnectTo("kurrentdb://localhost:2113")
    .RunWithGracefulShutdownAsync();
