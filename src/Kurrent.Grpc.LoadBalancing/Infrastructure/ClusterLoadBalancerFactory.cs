using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Factory for creating ClusterLoadBalancer instances.
/// </summary>
internal sealed class ClusterLoadBalancerFactory : LoadBalancerFactory {
    readonly IClusterNodePickingStrategy _strategy;
    readonly ILoggerFactory? _loggerFactory;

    public override string Name => "cluster";

    public ClusterLoadBalancerFactory(
        IClusterNodePickingStrategy strategy,
        ILoggerFactory? loggerFactory = null) {

        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _loggerFactory = loggerFactory;
    }

    public override LoadBalancer Create(LoadBalancerOptions options) =>
        new ClusterLoadBalancer(options.Controller, _strategy, _loggerFactory);
}
