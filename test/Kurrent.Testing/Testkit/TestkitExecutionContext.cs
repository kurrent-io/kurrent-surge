using System.Reactive.Linq;
using System.Reactive.Subjects;
using Kurrent.Testing.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;
using TUnit.Core.Logging;
using static Serilog.Core.Constants;

using ILogger = Serilog.ILogger;

namespace Kurrent.Testing.Testkit;

public static class TestkitExecutionContext {
    static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
        .Enrich.WithProperty(SourceContextPropertyName, nameof(TestkitExecutionContext))
        .Enrich.WithThreadId()
        .Enrich.WithProcessId()
        .Enrich.WithMachineName()
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .Enrich.WithDemystifiedStackTraces()
        .MinimumLevel.Debug();

    const string ConsoleOutputTemplate =
        "[{Timestamp:mm:ss.fff} {Level:u3}] {TestUid} ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}";

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

        Log.Debug("Testkit context initialized");
    }

    [Before(TestDiscovery)]
    public static void BeforeTestDiscovery() {
        Logger = new SerilogLoggerFactory(Log.Logger).CreateLogger(null!);
        TUnitLoggerFactory.AddSink<SerilogTUnitLogSink>();
    }

    public static Microsoft.Extensions.Logging.ILogger Logger { get; private set; }

    /// <summary>
    /// An observable subject that emits all log events.
    /// </summary>
    public static Subject<LogEvent> LogEvents { get; } = new();

    /// <summary>
    /// The application's configuration settings, built from various sources such as JSON files and environment variables.
    /// </summary>
    public static IConfiguration Configuration { get; }

    /// <summary>
    /// Creates a scoped logger for the specified test identified by <paramref name="testUid"/>.
    /// </summary>
    public static TestLogger GetTestLogger(Guid testUid) {
        var configuration = DefaultLoggerConfig
            .ReadFrom.Configuration(Configuration)
            .Enrich.WithProperty("TestUid", testUid)
            .Enrich.WithProperty("AppDomainId", AppDomain.CurrentDomain.Id)
            .WriteTo.OpenTelemetry(o => o.BatchingOptions.BufferingTimeLimit = TimeSpan.FromMilliseconds(100))
            .WriteTo.Console(
                // theme: Console.IsOutputRedirected ? ConsoleTheme.None : AnsiConsoleTheme.Code,
                theme: ConsoleTheme.None,
                outputTemplate: ConsoleOutputTemplate);

        return new TestLogger(testUid, configuration, LogEvents);
    }
}

public sealed class TestLogger : ILogger, IAsyncDisposable {
    public TestLogger(Guid testUid, LoggerConfiguration configuration, Subject<LogEvent> logEvents) {
        Logger = configuration.CreateLogger();

        var prop = new LogEventProperty("TestUid", new ScalarValue(testUid));

        // LogsSubscription = logEvents
        //     .Where(_ => TestContext.Current?.Id == testUid)
        //     .Subscribe(logEvent => logEvent.AddOrUpdateProperty(prop));

        LogsSubscription = logEvents
            .Where(_ => TestContext.Current?.Id == testUid.ToString())
            .Subscribe(logEvent => logEvent.AddOrUpdateProperty(prop));
    }

    Logger      Logger           { get; }
    IDisposable LogsSubscription { get; }

    public void Write(LogEvent logEvent) => Logger.Write(logEvent);

    public async ValueTask DisposeAsync() {
        Logger.Debug("Closing and flushing ...");
        await Logger.DisposeAsync().ConfigureAwait(false);
        LogsSubscription.Dispose();
    }
}
