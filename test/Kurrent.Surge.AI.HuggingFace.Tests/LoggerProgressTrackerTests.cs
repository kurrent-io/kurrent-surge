using Kurrent.Testkit.TUnit;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.AI.HuggingFace.Tests;

/// <summary>
/// Pure unit tests for <see cref="LoggerProgressTracker"/>'s throttling logic — no network, a capturing
/// <see cref="ILogger"/> in place of a real sink.
/// </summary>
[Timeout(10_000)]
public sealed class LoggerProgressTrackerTests {
    [Test]
    public async ValueTask logs_once_when_a_file_starts() {
        // Arrange
        var logger      = new CapturingLogger();
        var tracker     = new LoggerProgressTracker(logger);
        const int expectedLogCount = 1;

        // Act — a single report for a fresh file, mid-download (no step crossed, not complete).
        tracker.Report(new DownloadProgress("model.onnx", 5, 1_000, 0, 1));

        // Assert
        await Assert.That(logger.Entries.Count).IsEqualTo(expectedLogCount);
        await Assert.That(logger.Entries[0].Level).IsEqualTo(LogLevel.Information);
    }

    [Test]
    public async ValueTask logs_each_step_crossing_exactly_once() {
        // Arrange — stepFraction=0.10 over a 100-byte file; two reports (12, 19) land in the same 10-20%
        // bucket and must log only once for it; the next report (25) crosses into the following bucket.
        var logger  = new CapturingLogger();
        var tracker = new LoggerProgressTracker(logger, stepFraction: 0.10);
        const int expectedStepLogCount  = 2; // the 10% boundary, then the 20% boundary
        const int expectedTotalLogCount = 1 + expectedStepLogCount; // + the initial "started" log

        // Act
        tracker.Report(new DownloadProgress("model.onnx", 5, 100, 0, 1));  // started
        tracker.Report(new DownloadProgress("model.onnx", 12, 100, 0, 1)); // crosses 10%
        tracker.Report(new DownloadProgress("model.onnx", 19, 100, 0, 1)); // still 10-20%, no new log
        tracker.Report(new DownloadProgress("model.onnx", 25, 100, 0, 1)); // crosses 20%

        // Assert
        await Assert.That(logger.Entries.Count).IsEqualTo(expectedTotalLogCount);
    }

    [Test]
    public async ValueTask logs_completion_exactly_once() {
        // Arrange
        var logger  = new CapturingLogger();
        var tracker = new LoggerProgressTracker(logger);
        const int expectedLogCount = 2; // started + completed

        // Act
        tracker.Report(new DownloadProgress("model.onnx", 0, 100, 0, 1));   // started
        tracker.Report(new DownloadProgress("model.onnx", 100, 100, 0, 1)); // completes
        tracker.Report(new DownloadProgress("model.onnx", 100, 100, 0, 1)); // repeated completion, no duplicate log

        // Assert
        await Assert.That(logger.Entries.Count).IsEqualTo(expectedLogCount);
    }

    [Test]
    public async ValueTask falls_back_to_periodic_logging_when_total_size_is_unknown() {
        // Arrange — no TotalBytes: the step-fraction boundary logic doesn't apply, so progress logs every
        // 100 MB received instead.
        var logger  = new CapturingLogger();
        var tracker = new LoggerProgressTracker(logger);
        const long oneHundredMb    = 100L * 1024 * 1024;
        const int  expectedLogCount = 3; // started + the 100MB boundary + the 200MB boundary

        // Act
        tracker.Report(new DownloadProgress("model.gguf", 10, null, 0, 1));              // started
        tracker.Report(new DownloadProgress("model.gguf", oneHundredMb, null, 0, 1));     // crosses 100MB
        tracker.Report(new DownloadProgress("model.gguf", oneHundredMb + 50, null, 0, 1)); // no new boundary
        tracker.Report(new DownloadProgress("model.gguf", oneHundredMb * 2, null, 0, 1)); // crosses 200MB

        // Assert
        await Assert.That(logger.Entries.Count).IsEqualTo(expectedLogCount);
    }

    public sealed class InvalidStepFractionCases : TestCaseDataGenerator<double> {
        protected override IEnumerable<TestCase<double>> Data() {
            yield return new(0.0, DisplayName: "zero");
            yield return new(-0.1, DisplayName: "negative");
            yield return new(1.1, DisplayName: "greater than one");
        }
    }

    [Test]
    [InvalidStepFractionCases]
    public void rejects_step_fraction_outside_zero_to_one(double invalidStepFraction) {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoggerProgressTracker(new CapturingLogger(), invalidStepFraction));
    }

    /// <summary>Records every log entry so a test can assert what was logged (level + rendered message).</summary>
    sealed class CapturingLogger : ILogger {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
