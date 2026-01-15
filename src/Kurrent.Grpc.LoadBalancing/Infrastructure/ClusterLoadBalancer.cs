using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// gRPC LoadBalancer that manages subchannels based on cluster topology.
/// </summary>
internal sealed class ClusterLoadBalancer : SubchannelsLoadBalancer {
    readonly IClusterNodePickingStrategy _strategy;
    readonly ILogger? _logger;

    public ClusterLoadBalancer(
        IChannelControlHelper controller,
        IClusterNodePickingStrategy strategy,
        ILoggerFactory? loggerFactory = null)
        : base(controller, loggerFactory!) {

        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger = loggerFactory?.CreateLogger<ClusterLoadBalancer>();
    }

    /// <inheritdoc/>
    protected override SubchannelPicker CreatePicker(IReadOnlyList<Subchannel> readySubchannels) {
        _logger?.LogDebug("Creating picker with {Count} ready subchannels", readySubchannels.Count);
        return new ClusterPicker(readySubchannels, _strategy, _logger);
    }
}
