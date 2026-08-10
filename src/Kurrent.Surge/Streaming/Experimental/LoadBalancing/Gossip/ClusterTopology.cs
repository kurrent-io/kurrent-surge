using KurrentDB.Client;

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
    /// <summary>
    /// Returns the connectable nodes ordered by preference. Ties are shuffled.
    /// </summary>
    public IReadOnlyList<ClusterNode> RankNodes(NodeReadPreference preference) {
        return Nodes
            .Where(IsConnectable)
            .OrderBy(node => GetPriority(node.Role))
            .ThenBy(_ => Random.Shared.Next())
            .ToArray();

        static bool IsConnectable(ClusterNode node) =>
            node.IsAlive && node.Role != NodeRole.NotAllowed;

        int GetPriority(NodeRole role) =>
            preference switch {
                NodeReadPreference.Leader => role switch {
                    NodeRole.Leader   => 0,
                    NodeRole.Follower => 1,
                    _                 => 2
                },
                NodeReadPreference.Follower => role switch {
                    NodeRole.Follower => 0,
                    NodeRole.Leader   => 1,
                    _                 => 2
                },
                // Deviates from the legacy comparer (replica -> leader -> follower): a replica
                // preference expresses read offload, so fallback stays as far from the write
                // path as possible.
                NodeReadPreference.ReadOnlyReplica => role switch {
                    NodeRole.Replica  => 0,
                    NodeRole.Follower => 1,
                    _                 => 2
                },
                _ => 0
            };
    }
}
