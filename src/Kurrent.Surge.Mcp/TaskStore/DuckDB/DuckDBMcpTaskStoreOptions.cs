namespace Kurrent.Surge.Mcp.DuckDB;

/// <summary>
/// Configuration for <see cref="DuckDBMcpTaskStore"/>. Mirrors the tunables of the reference
/// in-memory 2.0 store — the poll interval and TTL advertised on newly created tasks — and adds the
/// sweep tunables the durable store needs: unlike the in-memory store, which loses expired tasks on
/// process restart, a file-backed store must expire and delete them itself.
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

    /// <summary>
    /// Time-to-live advertised on created tasks — the bound after which the store expires and then
    /// deletes them. Default 1 hour. SEP-2663 leaves the default to the server; its worked example
    /// advertises <c>3600000</c> ms (1 hour) at poll time, which this mirrors. A durable store never
    /// advertises unlimited: an unbounded lifetime would let the table grow without end. Must be
    /// positive — a non-positive value is rejected at construction.
    /// </summary>
    public TimeSpan DefaultTimeToLive { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How often the background sweep expires and deletes tasks past their time-to-live. Default 1
    /// minute. Must be positive: the sweep cannot be disabled, or a durable store would grow without
    /// bound. A non-positive value is rejected at construction.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a task is retained past its time-to-live before deletion. On expiry a task is first
    /// marked <c>failed</c> so a poll within this window observes the failure; once the grace period
    /// also elapses the row is deleted. Default 5 minutes.
    /// </summary>
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromMinutes(5);
}
