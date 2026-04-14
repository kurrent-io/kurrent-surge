using System.Text.Json.Serialization;

namespace Kurrent.Client.Admin;

/// <summary>
/// The state of a scavenge operation.
/// </summary>
[PublicAPI]
public enum ScavengeState {
    /// <summary>
    /// The scavenge is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// The scavenge completed successfully.
    /// </summary>
    [JsonStringEnumMemberName("Success")]
    Completed,

    /// <summary>
    /// The scavenge was manually stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// The scavenge failed with an error.
    /// </summary>
    Errored,

    /// <summary>
    /// The scavenge was interrupted by a node restart.
    /// </summary>
    Interrupted
}
