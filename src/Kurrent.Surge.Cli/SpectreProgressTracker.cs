namespace Kurrent.Surge.Cli;

/// <summary>
/// Renders per-file download progress as a Spectre <see cref="ProgressTask"/> — a new task appears when a
/// file starts, and advances as its chunks report bytes received.
/// </summary>
sealed class SpectreProgressTracker(ProgressContext context) : IProgressTracker {
    readonly Lock _lock = new();
    readonly Dictionary<string, ProgressTask> _tasks = new();

    /// <inheritdoc />
    public void Report(DownloadProgress value) {
        lock (_lock) {
            if (!_tasks.TryGetValue(value.File, out var task)) {
                task = context.AddTask(Markup.Escape(value.File), maxValue: value.TotalBytes ?? 1);
                _tasks[value.File] = task;
            }

            task.Value = value.BytesDownloaded;
        }
    }
}
