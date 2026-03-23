using System.Reactive.Subjects;
using Kurrent.Testing.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;
using static Serilog.Core.Constants;

namespace Kurrent.Testing.Testkit;

public static class TestkitExecutionContext {
    static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
        .Enrich.WithProperty(SourceContextPropertyName, nameof(TestkitExecutionContext))
        .Enrich.WithThreadId()
        .Enrich.WithProcessId()
        .Enrich.WithMachineName()
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .MinimumLevel.Debug();

    const string ConsoleOutputTemplate =
        "[{Timestamp:mm:ss.fff} {Level:u3}] {TestUid} ({ThreadId:000}) {SourceContext} | {Message}{Exception}{NewLine}";

    static long _initialized;

    static TestkitExecutionContext() {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0) {
            Log.Debug("Testkit context already initialized! Check your test setup.");
            throw new InvalidOperationException("Testkit context already initialized! Check your test setup.");
        }

        new OtelServiceMetadata("Testkit") {
            ServiceVersion   = "0.0.0",
            ServiceNamespace = "Kurrent.Testing",
        }.UpdateEnvironmentVariables();

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        Configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", true)
            .AddJsonFile($"appsettings.{environment}.json", true)                    // Accept default naming convention
            .AddJsonFile($"appsettings.{environment.ToLowerInvariant()}.json", true) // Linux is case-sensitive
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = DefaultLoggerConfig
            .ReadFrom.Configuration(Configuration)
            .Enrich.WithProperty("TestUid", Guid.Empty)
            .Enrich.WithProperty("AppDomainId", AppDomain.CurrentDomain.Id)
            .WriteTo.OpenTelemetry(o => o.BatchingOptions.BufferingTimeLimit = TimeSpan.FromMilliseconds(100))
            .WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
            .WriteTo.Console(theme: ConsoleTheme.None, outputTemplate: ConsoleOutputTemplate)
            .CreateLogger();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            Log.Debug("Testkit context process exiting, closing and flushing logger ...");
            Log.CloseAndFlush();
            _initialized = 0;
        };

        LoggerFactory = new SerilogLoggerFactory(Log.Logger);
        Logger        = LoggerFactory.CreateLogger(null!);

        Log.Debug("Testkit context initialized");
    }

    [Before(TestDiscovery)]
    public static void BeforeTestDiscovery() {
        Logger.LogInformation("Test discovery starting...");
    }

    public static Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory { get; private set; }

    public static Microsoft.Extensions.Logging.ILogger Logger { get; private set; }

    /// <summary>
    /// An observable subject that emits all log events.
    /// </summary>
    public static Subject<LogEvent> LogEvents { get; } = new();

    /// <summary>
    /// The application's configuration settings, built from various sources such as JSON files and environment variables.
    /// </summary>
    public static IConfiguration Configuration { get; }
}
