namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// The state of a cluster node as reported by gossip.
/// </summary>
/// <remarks>
/// Values match <c>event_store.client.gossip.MemberInfo.VNodeState</c> on the wire.
/// See <c>proto/kurrentdb/protocol/v1/gossip.proto</c>.
/// </remarks>
enum VNodeState {
    Initializing       = 0,
    DiscoverLeader     = 1,
    Unknown            = 2,
    PreReplica         = 3,
    CatchingUp         = 4,
    Clone              = 5,
    Follower           = 6,
    PreLeader          = 7,
    Leader             = 8,
    Manager            = 9,
    ShuttingDown       = 10,
    Shutdown           = 11,
    ReadOnlyLeaderless = 12,
    PreReadOnlyReplica = 13,
    ReadOnlyReplica    = 14,
    ResigningLeader    = 15
}

/// <summary>
/// A cluster member as reported by gossip.
/// </summary>
sealed record ClusterNode(VNodeState State, bool IsAlive, ClusterEndpoint Endpoint);

/// <summary>
/// A point-in-time view of the cluster membership.
/// </summary>
sealed record ClusterTopology(IReadOnlyList<ClusterNode> Nodes) {
    public static readonly ClusterTopology Empty = new([]);
}
