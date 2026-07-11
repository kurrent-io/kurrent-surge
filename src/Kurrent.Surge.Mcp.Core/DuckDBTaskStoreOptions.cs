namespace Kurrent.Surge.Mcp;

/// <summary>
/// Configuration for <see cref="DuckDBTaskStore"/>. Mirrors the tunables of the reference
/// in-memory MCP task store: TTL defaulting/clamping, poll interval, page size, background
/// cleanup cadence, and creation limits.
/// </summary>
[PublicAPI]
public sealed record DuckDBTaskStoreOptions {
    /// <summary>TTL applied to new tasks when the request does not specify one. Null = unlimited.</summary>
    public TimeSpan? DefaultTimeToLive { get; init; }

    /// <summary>Upper bound a requested TTL is silently clamped down to. Null = no cap.</summary>
    public TimeSpan? MaxTimeToLive { get; init; }

    /// <summary>Poll interval advertised on created tasks. Must be positive.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Cadence of the background expired-task sweep. Set to <see cref="Timeout.InfiniteTimeSpan"/>
    /// to disable the timer entirely (expiry is still enforced live on list/count queries).
    /// </summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum tasks returned per <c>ListTasks</c> page. Must be positive.</summary>
    public int PageSize { get; init; } = 100;

    /// <summary>Global cap on stored tasks. Null = unlimited. The count includes not-yet-swept expired tasks.</summary>
    public int? MaxTasks { get; init; }

    /// <summary>Per-session cap on active (non-expired) tasks. Null = unlimited. Never applies to the null session.</summary>
    public int? MaxTasksPerSession { get; init; }

    /// <summary>Validates the option invariants. Mirrors the reference store's constructor checks.</summary>
    /// <exception cref="ArgumentException">A default TTL exceeds the maximum TTL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A non-positive interval, page size, or limit.</exception>
    internal void Validate() {
        if (DefaultTimeToLive is { } defaultTtl && MaxTimeToLive is { } maxTtl && defaultTtl > maxTtl)
            throw new ArgumentException(
                $"Default TTL ({defaultTtl}) cannot exceed maximum TTL ({maxTtl}).",
                nameof(DefaultTimeToLive));

        if (PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "Poll interval must be positive.");

        if (PageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "Page size must be positive.");

        if (MaxTasks is <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTasks), MaxTasks, "Max tasks must be positive.");

        if (MaxTasksPerSession is <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTasksPerSession), MaxTasksPerSession, "Max tasks per session must be positive.");
    }
}
