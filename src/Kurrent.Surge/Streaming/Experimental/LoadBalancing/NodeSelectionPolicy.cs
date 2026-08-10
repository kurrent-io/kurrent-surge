using System.Collections.Frozen;
using KurrentDB.Client;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Ranks cluster nodes for connection eligibility based on the configured <see cref="NodePreference"/>.
/// </summary>
static class NodeSelectionPolicy {
    static readonly FrozenSet<VNodeState> NotAllowedStates = new[] {
        VNodeState.Manager,
        VNodeState.ShuttingDown,
        VNodeState.Shutdown,
        VNodeState.Unknown,
        VNodeState.Initializing,
        VNodeState.CatchingUp,
        VNodeState.ResigningLeader,
        VNodeState.PreLeader,
        VNodeState.PreReplica,
        VNodeState.PreReadOnlyReplica,
        VNodeState.Clone,
        VNodeState.DiscoverLeader
    }.ToFrozenSet();

    public static bool IsConnectable(ClusterNode node) =>
        node.IsAlive && !NotAllowedStates.Contains(node.State);

    public static int GetPriority(NodePreference preference, VNodeState state) =>
        preference switch {
            NodePreference.Leader => state switch {
                VNodeState.Leader             => 0,
                VNodeState.Follower           => 1,
                VNodeState.ReadOnlyReplica    => 2,
                VNodeState.PreReadOnlyReplica => 3,
                VNodeState.ReadOnlyLeaderless => 4,
                _                             => int.MaxValue
            },
            NodePreference.Follower => state switch {
                VNodeState.Follower           => 0,
                VNodeState.Leader             => 1,
                VNodeState.ReadOnlyReplica    => 2,
                VNodeState.PreReadOnlyReplica => 3,
                VNodeState.ReadOnlyLeaderless => 4,
                _                             => int.MaxValue
            },
            NodePreference.ReadOnlyReplica => state switch {
                VNodeState.ReadOnlyReplica    => 0,
                VNodeState.PreReadOnlyReplica => 1,
                VNodeState.ReadOnlyLeaderless => 2,
                VNodeState.Leader             => 3,
                VNodeState.Follower           => 4,
                _                             => int.MaxValue
            },
            _ => 0
        };

    /// <summary>
    /// Returns the connectable nodes ordered by preference. Ties are shuffled with <paramref name="random"/>.
    /// </summary>
    public static IReadOnlyList<ClusterNode> RankNodes(ClusterTopology topology, NodePreference preference, Random random) =>
        topology.Nodes
            .Where(IsConnectable)
            .OrderBy(node => GetPriority(preference, node.State))
            .ThenBy(_ => random.Next())
            .ToArray();
}
