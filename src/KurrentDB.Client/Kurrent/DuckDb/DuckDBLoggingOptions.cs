using Microsoft.Extensions.Logging;

namespace Kurrent.DuckDB;

/// <summary>
/// Configuration options for DuckDB logging.
/// </summary>
public sealed record DuckDBLoggingOptions {
    /// <summary>
    /// Default logging options with logging disabled.
    /// </summary>
    public static readonly DuckDBLoggingOptions Default = new();

    /// <summary>
    /// Gets whether logging is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the logging level.
    /// </summary>
    public LogLevel Level { get; init; } = LogLevel.Warning;

    /// <summary>
    /// Creates logging options with logging enabled at the specified level.
    /// </summary>
    public static DuckDBLoggingOptions Enable(LogLevel level = LogLevel.Warning) =>
        new() { Enabled = true, Level = level };
}
