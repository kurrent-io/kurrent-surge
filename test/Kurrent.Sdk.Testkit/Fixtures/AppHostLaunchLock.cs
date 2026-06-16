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
    /// </summary>
    public static async ValueTask<IDisposable> AcquireAsync(TimeSpan timeout, CancellationToken ct = default) {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

        // Scope the lock to the CI run, not a fixed or random path. Every test process in one run
        // shares GITHUB_RUN_ID, so within-run launch serialization (the lock's whole purpose) still
        // holds; a fresh path per run means a foreign-owned file left in the persistent runner's
        // /tmp by an earlier run (e.g. a step that ran as root) can never poison it — the previous
        // fixed path surfaced as UnauthorizedAccessException ("Permission denied") when reused. A
        // random per-call path would instead give every process its own file — no mutual exclusion.
        // Outside CI, falls back to a single fixed key where one developer owns the file.
        var runScope = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local";
        var lockPath = Path.Combine(Path.GetTempPath(), $"kurrent-testkit-apphost-{runScope}.lock");


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
