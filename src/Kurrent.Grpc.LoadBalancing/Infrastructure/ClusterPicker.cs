using System.Net;
using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// SubchannelPicker that wraps <see cref="IClusterNodePickingStrategy"/>.
/// Builds context and delegates to strategy for clean abstraction.
/// </summary>
/// <remarks>
/// <para>
/// This class bridges gRPC's complex <see cref="SubchannelPicker"/> API to our
/// clean <see cref="IClusterNodePickingStrategy"/> abstraction.
/// </para>
/// <para>
/// The picker builds <see cref="SubchannelNode"/> array once at construction
/// (cold path), then each Pick() creates context and delegates to strategy.
/// </para>
/// </remarks>
internal sealed class ClusterPicker : SubchannelPicker {
    readonly SubchannelNode[] _nodes;
    readonly IClusterNodePickingStrategy _strategy;
    readonly ILogger? _logger;

    public ClusterPicker(
        IReadOnlyList<Subchannel> readySubchannels,
        IClusterNodePickingStrategy strategy,
        ILogger? logger = null) {

        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger = logger;

        // Build SubchannelNode[] ONCE at construction (cold path)
        _nodes = readySubchannels
            .Select(sc => new SubchannelNode(sc, ExtractNode(sc)))
            .Where(sn => sn.Node.NodeId != Guid.Empty) // Filter out invalid nodes
            .ToArray();

        _logger?.LogTrace("Picker created with {Count} nodes", _nodes.Length);
    }

    /// <summary>
    /// Picks a subchannel for the next call by delegating to strategy.
    /// </summary>
    public override PickResult Pick(PickContext context) {
        // Build context with nodes and HTTP request
        var pickContext = new PickNodeContext(_nodes, context.Request);

        // Delegate to strategy
        var result = _strategy.Pick(in pickContext);

        // Convert to gRPC PickResult
        return result.ToPickResult();
    }

    static ClusterNode ExtractNode(Subchannel subchannel) {
        var address = subchannel.CurrentAddress;
        if (address == null)
            return default;

        // Prefer full ClusterNode if stored, otherwise reconstruct from parts
        if (address.Attributes.TryGetValue(ClusterNodeAttributes.Node, out var node))
            return node;

        return new ClusterNode {
            NodeId = address.Attributes.TryGetValue(ClusterNodeAttributes.NodeId, out var id) ? id : Guid.Empty,
            EndPoint = new DnsEndPoint(address.EndPoint.Host, address.EndPoint.Port),
            Role = address.Attributes.TryGetValue(ClusterNodeAttributes.Role, out var role) ? role : NodeRole.None,
            Status = address.Attributes.TryGetValue(ClusterNodeAttributes.Status, out var status) ? status : NodeStatus.Starting
        };
    }
}
