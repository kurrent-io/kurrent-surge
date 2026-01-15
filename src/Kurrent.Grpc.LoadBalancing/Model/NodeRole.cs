namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Represents the functional role a node is AUTHORIZED to perform.
/// This is the "what" axis - what the node can do.
/// </summary>
public enum NodeRole {
    /// <summary>No defined role yet (initializing, unknown).</summary>
    None = 0,

    /// <summary>Authoritative for writes and coordination (primary/master).</summary>
    Leader = 1,

    /// <summary>Voting member that replicates data (secondary/replica).</summary>
    Follower = 2,

    /// <summary>Non-voting, read-scaling member (read replica).</summary>
    ReadOnly = 3,

    /// <summary>Administrative/orchestration only (not for client traffic).</summary>
    Manager = 4,

    /// <summary>Leader handing off authority (stepping down).</summary>
    Resigning = 5
}
