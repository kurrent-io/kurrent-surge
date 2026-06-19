using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Kurrent.Streaming.Tests.Fakes;

/// <summary>
/// An <see cref="ILoggerFactory"/> that records every log entry so tests can assert what the drone
/// logged (level + rendered message). Abstractions-only — no logging package required.
/// </summary>
sealed class CapturingLoggerFactory : ILoggerFactory {
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }

    sealed class CapturingLogger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
