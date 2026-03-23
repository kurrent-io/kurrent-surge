using Kurrent.Testing.TUnit.Enrichers;
using Serilog.Events;
using TUnit.Core.Logging;

namespace Kurrent.Testing.Testkit;

public class SerilogLogSink : ILogSink {
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

    public ValueTask LogAsync(LogLevel level, string message, Exception? exception, Context? context) =>
        ValueTask.CompletedTask; // TUnit calls both Log and LogAsync; only handle sync to avoid duplication

    public void Log(LogLevel level, string message, Exception? exception, Context? context) {
        if (!IsEnabled(level)) return;

        var serilogLevel = level switch {
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning     => LogEventLevel.Warning,
            LogLevel.Error       => LogEventLevel.Error,
            LogLevel.Critical    => LogEventLevel.Fatal,
            _                    => LogEventLevel.Information
        };

        if (context is TestContext testContext) {
            Serilog.Log.Logger
                .ForContext("TestUid", testContext.TestId)
                .Write(serilogLevel, exception, message);
        } else {
            Serilog.Log.Logger
                .Write(serilogLevel, exception, message);
        }
    }
}

