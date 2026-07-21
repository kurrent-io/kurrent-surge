using System.Net.Http;

namespace Kurrent.Surge.Cli;

static class CliHelpers {
    /// <summary>Runs <paramref name="action"/>, turning a <see cref="ModelException"/> or
    /// <see cref="HttpRequestException"/> into a red error line on <paramref name="errorConsole"/> and exit
    /// code 1 instead of letting it propagate.</summary>
    public static async Task<int> ExecuteAsync(IAnsiConsole errorConsole, Func<Task<int>> action) {
        try {
            return await action().ConfigureAwait(false);
        } catch (Exception ex) when (ex is ModelException or HttpRequestException) {
            errorConsole.MarkupLineInterpolated($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    /// <summary>Renders a byte count as a human-friendly size, e.g. <c>3.14 MB</c>.</summary>
    public static string Humanize(long bytes) {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double   size  = bytes;
        var      unit  = 0;

        while (size >= 1024 && unit < units.Length - 1) {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    /// <summary>
    /// Shared by <c>cache clear</c> and <c>models clear &lt;repo&gt;</c>: deletes each grouped model directory
    /// independently so one locked model never blocks freeing the rest, recording (not throwing)
    /// <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>. Callers own their own
    /// success/failure messages — this returns only the raw counts.
    /// </summary>
    public static (int RemovedCount, long FreedBytes, List<(string Directory, string Message)> Failures) DeleteModelDirectories(
        IEnumerable<IGrouping<string, CachedModel>> byDirectory
    ) {
        List<(string Directory, string Message)> failures = [];
        var  removedCount = 0;
        long freedBytes   = 0;

        foreach (var group in byDirectory) {
            try {
                Directory.Delete(group.Key, recursive: true);
                removedCount++;
                freedBytes += group.Sum(m => m.TotalBytes);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                failures.Add((group.Key, ex.Message));
            }
        }

        return (removedCount, freedBytes, failures);
    }

    /// <summary>Renders each <see cref="DeleteModelDirectories"/> failure as a red error line.</summary>
    public static void ReportFailures(IAnsiConsole errorConsole, List<(string Directory, string Message)> failures) {
        foreach (var (directory, message) in failures)
            errorConsole.MarkupLineInterpolated($"[red]Error:[/] Could not delete [bold]{Markup.Escape(directory)}[/]: {Markup.Escape(message)}");
    }
}
