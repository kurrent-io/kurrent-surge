using Kurrent.Testkit.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Exceptions;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;

using static Serilog.Core.Constants;

namespace Kurrent.Testkit.Testkit;

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
        
        Environment.SetEnvironmentVariable("ASPIRE_VERSION_CHECK_DISABLED", "true");
        Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
        Environment.SetEnvironmentVariable("ASPIRE_CLI_TELEMETRY_OPTOUT", "true");
        Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_TELEMETRY_OPTOUT", "true");
        Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
        
        // Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:4317"); // default is http://localhost:18889
       
        // Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_INSECURE", "true");
        // Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_CORS_ALLOWED_ORIGINS", "*");
        // Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_FORWARDEDHEADERS_ENABLED", "true"); // default is false
        
        // Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_AI_DISABLED", "true");              // default is false
        //
        // // Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN", "kurrent");
        
        new OtelServiceMetadata("Testkit") {
            ServiceVersion   = "0.0.0",
            ServiceNamespace = "Kurrent.Testkit",
        }.UpdateEnvironmentVariables();

        var environment  = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var otlpEndpoint = Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL") ?? "http://localhost:4317";
        
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
            .WriteTo.OpenTelemetry(o => {
                o.Endpoint = otlpEndpoint;
                o.BatchingOptions.BufferingTimeLimit = TimeSpan.FromMilliseconds(100);
            })
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

    public static Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory { get; private set; }

    public static Microsoft.Extensions.Logging.ILogger Logger { get; private set; }

    /// <summary>
    /// The application's configuration settings, built from various sources such as JSON files and environment variables.
    /// </summary>
    public static IConfiguration Configuration { get; }
}
