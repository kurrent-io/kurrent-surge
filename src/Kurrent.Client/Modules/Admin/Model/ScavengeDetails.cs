namespace Kurrent.Client.Admin;

/// <summary>
/// Projected state of a scavenge operation, assembled from events in <c>$scavenges-{id}</c>.
/// </summary>
[PublicAPI]
public record ScavengeDetails {
    public string                ScavengeId    { get; init; } = "";
    public string                NodeEndpoint  { get; init; } = "";
    public ScavengeState         State         { get; init; }
    public DateTime              StartedAt     { get; init; }
    public DateTime              CompletedAt   { get; init; }
    public TimeSpan              Duration      { get; init; }
    public string                Error         { get; init; } = "";
    public ScavengeConfiguration Configuration { get; init; } = new();
    public ScavengeProgress      Progress      { get; init; } = new();
}

/// <summary>
/// Configuration parameters for a scavenge operation.
/// </summary>
[PublicAPI]
public record ScavengeConfiguration {
    public int  Threads        { get; init; }
    public int  StartFromChunk { get; init; }
    public bool MergeChunks    { get; init; }
}

/// <summary>
/// Aggregated progress of a scavenge operation.
/// </summary>
[PublicAPI]
public record ScavengeProgress {
    public long SpaceSaved           { get; init; }
    public int  ChunksScavenged      { get; init; }
    public int  ChunksSkipped        { get; init; }
    public int  ChunksMerged         { get; init; }
    public int  IndexTablesScavenged { get; init; }
    public long IndexEntriesDeleted  { get; init; }
}
