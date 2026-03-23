// ReSharper disable CheckNamespace

using Serilog.Events;
using TUnit.Core.Logging;
using static Serilog.Log;

namespace Kurrent.Testkit.TUnit.Logging;

public class SerilogTUnitLogSink : ILogSink {
    public bool IsEnabled(LogLevel level) =>
        Logger.IsEnabled(MapToSerilogLogLevel(level));

    static LogEventLevel MapToSerilogLogLevel(LogLevel level) =>
        level switch {
            LogLevel.Trace       => LogEventLevel.Verbose,
            LogLevel.Debug       => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning     => LogEventLevel.Warning,
            LogLevel.Error       => LogEventLevel.Error,
            LogLevel.Critical    => LogEventLevel.Fatal,
            _                    => LogEventLevel.Information
        };

    public ValueTask LogAsync(LogLevel level, string message, Exception? exception, Context? context) =>
        ValueTask.CompletedTask; // TUnit calls both Log and LogAsync; only handle sync to avoid duplication

    public void Log(LogLevel level, string message, Exception? exception, Context? context) {
        var serilogLevel = MapToSerilogLogLevel(level);

        if (!Logger.IsEnabled(MapToSerilogLogLevel(level)))
            return;

        if (context is TestContext testContext)
            Logger.ForContext("TestUid", testContext.TestId)
                .Write(serilogLevel, exception, message);
        else
            Logger.Write(serilogLevel, exception, message);
    }
}

