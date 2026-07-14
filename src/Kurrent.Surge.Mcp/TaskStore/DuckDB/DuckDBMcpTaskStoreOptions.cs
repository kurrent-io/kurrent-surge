namespace Kurrent.Surge.Mcp.DuckDB;

/// <summary>
/// Configuration for <see cref="DuckDBMcpTaskStore"/>. Mirrors the tunables of the reference
/// in-memory 2.0 store: the poll interval and TTL advertised on newly created tasks.
/// </summary>
[PublicAPI]
public sealed record DuckDBMcpTaskStoreOptions {
    /// <summary>
    /// Default configuration section these options are bound from when registered via
    /// <c>AddDuckDBMcpTaskStore</c> without an explicit instance.
    /// </summary>
    public const string ConfigurationSection = "McpTaskStore";

    /// <summary>Poll interval (milliseconds) advertised on created tasks. Default 1000.</summary>
    public long DefaultPollIntervalMs { get; set; } = 1000;

    /// <summary>Time-to-live advertised on created tasks, or <see langword="null"/> for unlimited.</summary>
    public TimeSpan? DefaultTimeToLive { get; set; }
}
