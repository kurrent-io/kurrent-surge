using System.Diagnostics;

namespace Kurrent.Toolkit.Diagnostics;

static class TelemetryConstants {
    /// <summary>
    /// The source name used for <see cref="DiagnosticListener"/> and <see cref="ActivitySource"/>.
    /// </summary>
    public const string SourceName = "Kurrent.Surge.Processors";

    /// <summary>
    /// The version used for <see cref="ActivitySource"/>.
    /// </summary>
    public const string Version = "0.9.0";

    /// <summary>
    /// Shared diagnostic listener for all drones.
    /// </summary>
    public static readonly DiagnosticListener Listener = new(SourceName);

    /// <summary>
    /// Shared activity source for all drones.
    /// </summary>
    public static readonly ActivitySource Activities = new(SourceName, Version);
}
