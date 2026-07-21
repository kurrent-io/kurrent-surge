using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.AI.HuggingFace;

/// <summary>
/// Logs per-file download progress at <see cref="LogLevel.Information"/>: once when a file starts, once per
/// <paramref name="stepFraction"/> of its size crossed, and once on completion. When a file's total size is
/// unknown, logs every 100 MB received instead of step boundaries.
/// </summary>
/// <param name="logger">The logger progress is written to.</param>
/// <param name="stepFraction">The fraction of a file's size between step log entries; must be in the range (0, 1].</param>
public sealed class LoggerProgressTracker(ILogger logger, double stepFraction = 0.10) : IProgressTracker {
    const long HundredMegabytes = 100L * 1024 * 1024;

    readonly double _stepFraction = stepFraction is > 0 and <= 1
        ? stepFraction
        : throw new ArgumentOutOfRangeException(nameof(stepFraction), stepFraction, "Must be in the range (0, 1].");

    readonly Lock _lock = new();

    // Mutable state guarded by _lock — files download sequentially, but a file's own chunks report
    // progress concurrently, so callbacks for the same file can race each other.
    string? _currentFile;
    int     _lastStep;
    long    _lastLoggedBytes;
    bool    _completed;

    /// <inheritdoc />
    public void Report(DownloadProgress value) {
        lock (_lock) {
            if (_currentFile != value.File) {
                _currentFile     = value.File;
                _lastStep        = 0;
                _lastLoggedBytes = 0;
                _completed       = false;

                logger.LogInformation(
                    "Download started: {File} ({FileNumber}/{FileCount}) {Percent}% ({BytesDownloaded}/{TotalBytes} bytes)",
                    value.File, value.FileIndex + 1, value.FileCount, PercentOf(value), value.BytesDownloaded, value.TotalBytes);
            }

            if (value.TotalBytes is not { } total) {
                if (value.BytesDownloaded - _lastLoggedBytes >= HundredMegabytes) {
                    _lastLoggedBytes = value.BytesDownloaded;

                    logger.LogInformation(
                        "Download progress: {File} ({FileNumber}/{FileCount}) {BytesDownloaded} bytes (size unknown)",
                        value.File, value.FileIndex + 1, value.FileCount, value.BytesDownloaded);
                }

                return;
            }

            if (value.BytesDownloaded >= total) {
                if (_completed)
                    return;

                _completed = true;

                logger.LogInformation(
                    "Download completed: {File} ({FileNumber}/{FileCount}) {Percent}% ({BytesDownloaded}/{TotalBytes} bytes)",
                    value.File, value.FileIndex + 1, value.FileCount, PercentOf(value), value.BytesDownloaded, value.TotalBytes);

                return;
            }

            var step = (int)((double)value.BytesDownloaded / total / _stepFraction);

            if (step <= _lastStep)
                return;

            _lastStep = step;

            logger.LogInformation(
                "Download progress: {File} ({FileNumber}/{FileCount}) {Percent}% ({BytesDownloaded}/{TotalBytes} bytes)",
                value.File, value.FileIndex + 1, value.FileCount, PercentOf(value), value.BytesDownloaded, value.TotalBytes);
        }
    }

    static int? PercentOf(DownloadProgress value) =>
        value.Fraction is { } fraction ? (int)Math.Round(fraction * 100) : null;
}
