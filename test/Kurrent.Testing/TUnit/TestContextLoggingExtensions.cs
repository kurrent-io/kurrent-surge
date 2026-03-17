using System.Diagnostics.CodeAnalysis;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace Kurrent.Testing.TUnit;

public static class TestContextLoggingExtensions {
    const string LoggerFactoryKey = "$TestLoggerFactory";
    const string LoggerKey        = "$TestLogger";

    extension(TestContext? ctx) {
        public Serilog.ILogger AddLogging(TestLogger testLogger) {
            // Check if logger already exists (idempotent operation)
            if (ctx.ContainsItem<ILoggerFactory>(LoggerFactoryKey))
                return testLogger;

            // Create and inject new logger factory
            ILoggerFactory factory = new SerilogLoggerFactory(testLogger, dispose: true);
            ctx.InjectItem(factory, LoggerFactoryKey);
            ctx.InjectItem(factory.CreateLogger(null!), LoggerKey);
            return testLogger;
        }
    }

    extension(TestContext) {
        public static bool TryGetLoggerFactory([MaybeNullWhen(false)] out ILoggerFactory factory) =>
            TestContext.Current.TryExtractItem(LoggerFactoryKey, out factory);

        public static ILoggerFactory CurrentLoggerFactory =>
            TestContext.Current.ExtractRequiredItem<ILoggerFactory>(LoggerFactoryKey);

        public static ILogger CurrentLogger =>
            TestContext.Current.ExtractRequiredItem<ILogger>(LoggerKey);
    }
}
