namespace Kurrent.Client.Admin;

/// <summary>
/// Lightweight summary of a scavenge operation, used by <see cref="AdminClient.ListScavenges"/>.
/// </summary>
[PublicAPI]
public record ScavengeSummary {
    public string ScavengeId { get; init; } = "";
    public string NodeEndpoint { get; init; } = "";
    public ScavengeState State { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public long SpaceSaved { get; init; }
}
