namespace Kurrent.Client.Admin;

/// <summary>
/// Acknowledged response from a scavenge command (start or stop).
/// This represents what the server did with the request, not the outcome of the scavenge run.
/// </summary>
[PublicAPI]
public readonly record struct ScavengeResponse(string ScavengeId, ScavengeResponseStatus Status);

/// <summary>
/// The server's acknowledgment status after a scavenge command.
/// </summary>
[PublicAPI]
public enum ScavengeResponseStatus {
    /// <summary>A new scavenge was accepted and has started.</summary>
    Started,

    /// <summary>A scavenge was already running; the returned ID is the in-progress job.</summary>
    AlreadyRunning,

    /// <summary>A stop was requested for the given scavenge.</summary>
    StopRequested
}
