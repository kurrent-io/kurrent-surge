namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// The routing role of a cluster member. <see cref="NotAllowed"/> covers every wire state
/// that must not receive traffic — transitional, administrative, dead, or unknown — and is
/// the default so an uninitialized member is unroutable by construction.
/// </summary>
enum NodeRole {
    NotAllowed = 0,
    Leader     = 1,
    Follower   = 2,
    Replica    = 3
}

/// <summary>
/// A cluster member as reported by gossip.
/// </summary>
sealed record ClusterNode(NodeRole Role, bool IsAlive, ClusterEndpoint Endpoint);

/// <summary>
/// A point-in-time view of the cluster membership.
/// </summary>
sealed record ClusterTopology(IReadOnlyList<ClusterNode> Nodes) {
    public static readonly ClusterTopology Empty = new([]);
}
