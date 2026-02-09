using Kurrent.Client.Streams;
using Processor.ClickstreamSessions;
using Serilog;

// Clickstream Session Analytics processor
// Groups page views into user sessions based on 30-minute inactivity timeout
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var processor = new SessionAnalyticsProcessor();

await KurrentProcessors.Create(processor)
    .ConnectTo("kurrentdb://localhost:2113")
    .WithFilterExpression("schema = 'Processor.ClickstreamSessions.Events.PageView'")
    .RunWithGracefulShutdownAsync();
