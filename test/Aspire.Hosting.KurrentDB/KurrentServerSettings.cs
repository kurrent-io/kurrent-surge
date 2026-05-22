namespace Aspire.Hosting.KurrentDB;

/// <summary>
/// KurrentDB server configuration settings that map to <c>KURRENTDB_*</c> environment variables.
/// Only models the settings a test author would reasonably configure — infrastructure
/// settings managed by Aspire (certs, networking, gossip) are excluded.
/// Use <see cref="ExtraEnvironmentVariables"/> for anything not modeled here.
/// </summary>
public sealed class KurrentServerSettings {
    /// <summary>
    /// The log level for the KurrentDB node.
    /// Maps to <c>KURRENTDB_LOG_LEVEL</c>.
    /// </summary>
    public KurrentDBLogLevel KurrentDBLogLevel { get; set; } = KurrentDBLogLevel.Debug;

    /// <summary>
    /// Projection settings for the KurrentDB node.
    /// </summary>
    public ProjectionSettings Projections { get; set; } = new();

    /// <summary>
    /// Append operation settings for the KurrentDB node.
    /// </summary>
    public AppendSettings Append { get; set; } = new();

    /// <summary>
    /// Produces a dictionary of <c>KURRENTDB_*</c> environment variables for all settings.
    /// </summary>
    public Dictionary<string, string> ToEnvironmentVariables() =>
        new(StringComparer.OrdinalIgnoreCase) {
            ["KURRENTDB_LOG_LEVEL"]                  = Enum.GetName(KurrentDBLogLevel)!,
            ["KURRENTDB_RUN_PROJECTIONS"]            = Enum.GetName(Projections.RunProjections)!,
            ["KURRENTDB_START_STANDARD_PROJECTIONS"] = Projections.StartStandardProjections.ToString().ToLower(),
            ["KURRENTDB_MAX_APPEND_SIZE"]            = Append.MaxRequestSize.ToString(),
            ["KURRENTDB_MAX_APPEND_EVENT_SIZE"]      = Append.MaxRecordSize.ToString()
        };
}

/// <summary>
/// Specifies the meaning and relative importance of a log event.
/// </summary>
public enum KurrentDBLogLevel {
    /// <summary>
    /// Anything and everything you might want to know about
    /// a running block of code.
    /// </summary>
    Verbose,

    /// <summary>
    /// Internal system events that aren't necessarily
    /// observable from the outside.
    /// </summary>
    Debug,

    /// <summary>
    /// The lifeblood of operational intelligence - things
    /// happen.
    /// </summary>
    Information,

    /// <summary>
    /// Service is degraded or endangered.
    /// </summary>
    Warning,

    /// <summary>
    /// Functionality is unavailable, invariants are broken
    /// or data is lost.
    /// </summary>
    Error,

    /// <summary>
    /// If you have a pager, it goes off when one of these
    /// occurs.
    /// </summary>
    Fatal
}

/// <summary>
/// Projection settings for the KurrentDB node.
/// </summary>
public sealed class ProjectionSettings {
    /// <summary>
    /// Enables the running of projections.
    /// Maps to <c>KURRENTDB_RUN_PROJECTIONS</c>.
    /// </summary>
    public ProjectionType RunProjections { get; set; } = ProjectionType.All;

    /// <summary>
    /// Start the built-in system projections.
    /// Maps to <c>KURRENTDB_START_STANDARD_PROJECTIONS</c>.
    /// </summary>
    public bool StartStandardProjections { get; set; } = true;
}

/// <summary>
/// Append operation settings for the KurrentDB node.
/// </summary>
public sealed class AppendSettings {
    /// <summary>
    /// The maximum total size of all records in an append request, in bytes.
    /// Maps to <c>KURRENTDB_MAX_APPEND_SIZE</c>.
    /// </summary>
    public int MaxRequestSize { get; set; } = 268_435_456;

    /// <summary>
    /// The maximum size of an individual record in an append request, in bytes.
    /// Maps to <c>KURRENTDB_MAX_APPEND_EVENT_SIZE</c>.
    /// </summary>
    public int MaxRecordSize { get; set; } = 16_777_216;
}

/// <summary>
/// Controls which projections the KurrentDB node runs.
/// </summary>
public enum ProjectionType {
    /// <summary>Projections are completely disabled.</summary>
    None,

    /// <summary>Only built-in system projections run ($by_category, $by_event_type, etc.).</summary>
    System,

    /// <summary>System projections and user-defined JavaScript projections.</summary>
    All
}

