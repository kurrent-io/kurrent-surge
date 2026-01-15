using System.Collections.Immutable;
using System.Net;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Protocol-agnostic representation of a cluster node.
/// Separates "Role" (Authority) from "Status" (Health).
/// </summary>
/// <remarks>
/// <para>
/// This struct is the universal abstraction for all cluster node types.
/// Protocol-specific adapters create <see cref="ClusterNode"/> instances
/// from their native representations.
/// </para>
/// <para>
/// The <see cref="Metadata"/> dictionary handles protocol-specific extras
/// without polluting the core model.
/// </para>
/// </remarks>
public readonly record struct ClusterNode() {
    /// <summary>Unique identifier for this node instance.</summary>
    public required Guid NodeId { get; init; }

    /// <summary>Network endpoint for connecting to this node.</summary>
    public required DnsEndPoint EndPoint { get; init; }

    /// <summary>The functional role this node is authorized to perform.</summary>
    public required NodeRole Role { get; init; }

    /// <summary>The node's current health and synchronization status.</summary>
    public required NodeStatus Status { get; init; }

    /// <summary>
    /// Protocol-specific metadata (e.g., original state enum, term, epoch).
    /// </summary>
    public ImmutableDictionary<string, object> Metadata { get; init; }
        = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Can this node serve read requests?
    /// True when node is Ready or Isolated (stale reads acceptable).
    /// </summary>
    public bool CanServeReads => Status is NodeStatus.Ready or NodeStatus.Isolated;

    /// <summary>
    /// Can this node accept write requests?
    /// True only when node is Leader AND Ready.
    /// </summary>
    public bool CanAcceptWrites => Role == NodeRole.Leader && Status == NodeStatus.Ready;

    /// <summary>
    /// Is this node eligible for client traffic?
    /// True when node is Ready or Isolated.
    /// </summary>
    public bool IsEligible => Status is NodeStatus.Ready or NodeStatus.Isolated;
}
