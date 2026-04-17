using Kurrent.Client.Streams;
using Kurrent.Processors;
using Processor.ClickstreamSessions;
using Serilog;

// Clickstream Session Analytics processor
// Groups page views into user sessions based on 30-minute inactivity timeout
// Press Ctrl+C to gracefully shutdown

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await KurrentProcessor.Create(opts => {
    opts.Connection("kurrentdb://localhost:2113");
    opts.Subscription.Filter = new ExpressionFilter("schema = 'Processor.ClickstreamSessions.Events.PageView'");
    opts.Subscribe.Use(new SessionAnalyticsProcessor());
}).RunWithGracefulShutdownAsync();
