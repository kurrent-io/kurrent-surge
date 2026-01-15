using Grpc.Net.Client.Balancer;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Attribute keys for ClusterNode data on BalancerAddress.
/// </summary>
/// <remarks>
/// <para>
/// Prefer storing the full <see cref="Node"/> attribute when possible.
/// The individual NodeId/Role/Status attributes are kept for backward
/// compatibility and debugging visibility.
/// </para>
/// </remarks>
public static class ClusterNodeAttributes {
    /// <summary>Full ClusterNode (preferred - avoids reconstruction).</summary>
    public static readonly BalancerAttributesKey<ClusterNode> Node = new("cluster-node");

    /// <summary>Node identifier (for debugging/logging).</summary>
    public static readonly BalancerAttributesKey<Guid> NodeId = new("cluster-node-id");

    /// <summary>Node role (for debugging/logging).</summary>
    public static readonly BalancerAttributesKey<NodeRole> Role = new("cluster-node-role");

    /// <summary>Node status (for debugging/logging).</summary>
    public static readonly BalancerAttributesKey<NodeStatus> Status = new("cluster-node-status");
}
