namespace Kurrent.Surge.Mcp;

/// <summary>
/// Configuration for <see cref="DuckDBMcpTaskStore"/>. Mirrors the tunables of the reference
/// in-memory 2.0 store: the poll interval and TTL advertised on newly created tasks.
/// </summary>
[PublicAPI]
public sealed record DuckDBTaskStoreOptions {
    /// <summary>
    /// Default configuration section these options are bound from when registered via
    /// <c>AddDuckDBMcpTaskStore</c> without an explicit instance.
    /// </summary>
    public const string ConfigurationSection = "DuckDBMcpTaskStore";

    /// <summary>Poll interval (milliseconds) advertised on created tasks. Default 1000.</summary>
    public long DefaultPollIntervalMs { get; set; } = 1000;

    /// <summary>Time-to-live advertised on created tasks, or <see langword="null"/> for unlimited.</summary>
    public TimeSpan? DefaultTimeToLive { get; set; }
}
