using Serilog.Events;
using TUnit.Core.Logging;

namespace Kurrent.Testing.Testkit;

// public class TestHeaderLogger(Context context) : DefaultLogger(context) {
//     bool _hasOutputHeader;
//
//     protected override string GenerateMessage(string message, Exception? exception, LogLevel logLevel) {
//         var baseMessage = base.GenerateMessage(message, exception, logLevel);
//
//         if (!_hasOutputHeader && Context is TestContext testContext) {
//             _hasOutputHeader = true;
//             var testId = $"{testContext.Metadata.TestDetails.ClassType.Name}.{testContext.Metadata.TestName}";
//
//             return $"--- {testId} ---\n{baseMessage}";
//         }
//
//         return baseMessage;
//     }
// }

public class SerilogTUnitLogSink : ILogSink {
    readonly Serilog.ILogger _logger = Serilog.Log.ForContext<SerilogTUnitLogSink>();

    public bool IsEnabled(LogLevel level) => true;

    public ValueTask LogAsync(LogLevel level, string message, Exception? exception, Context? context) {
        Log(level, message, exception, context);
        return ValueTask.CompletedTask;
    }

    public void Log(LogLevel level, string message, Exception? exception, Context? context) {
        var serilogLevel = level switch {
            LogLevel.Trace       => LogEventLevel.Verbose,
            LogLevel.Debug       => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning     => LogEventLevel.Warning,
            LogLevel.Error       => LogEventLevel.Error,
            LogLevel.Critical    => LogEventLevel.Fatal,
            _                    => LogEventLevel.Information
        };

        _logger
            .ForContext("TestUid", context.TestId)
            .Write(serilogLevel, exception, message);
    }
}
