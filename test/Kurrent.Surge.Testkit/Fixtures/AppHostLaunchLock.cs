using System.Diagnostics;

namespace Kurrent.Testkit.Aspire;

/// <summary>
/// Machine-wide lock that serializes Aspire AppHost launches across test processes.
/// Temporary workaround for https://github.com/dotnet/aspire/issues/10146 — remove
/// together with its call site once the upstream issue is fixed.
/// </summary>
static class AppHostLaunchLock {
    /// <summary>
    /// Acquires the launch lock, waiting until the holder releases it or the timeout elapses.
    /// Processes that pass the same <paramref name="key"/> serialize against each other; a distinct
    /// key is an independent lock. <paramref name="key"/> scopes the on-disk lock file, so it must be
    /// a filesystem-safe token and identical across every process that needs to be serialized.
    /// </summary>
    public static async ValueTask<IDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default) {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        var lockPath = Path.Combine(Path.GetTempPath(), $"kurrent-testkit-apphost-{key}.lock");

        while (true) {
            try {
                // FileShare.None takes an OS-level exclusive lock that the kernel releases
                // automatically when the holding process exits — crashed test hosts cannot
                // leave a stale lock behind. The file itself is intentionally never deleted:
                // unlink-on-close would let a waiter lock a dead inode while a third process
                // recreates the path, breaking mutual exclusion on unix.
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (Stopwatch.GetTimestamp() < deadline) {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
            }
        }
    }
}
