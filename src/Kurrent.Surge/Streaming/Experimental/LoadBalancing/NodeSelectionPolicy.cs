using KurrentDB.Client;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Ranks cluster nodes for connection eligibility based on the configured <see cref="NodeReadPreference"/>.
/// </summary>
static class NodeSelectionPolicy {
    /// <summary>
    /// Returns the connectable nodes ordered by preference. Ties are shuffled.
    /// </summary>
    public static IReadOnlyList<ClusterNode> RankNodes(ClusterTopology topology, NodeReadPreference preference) {
        return topology.Nodes
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
