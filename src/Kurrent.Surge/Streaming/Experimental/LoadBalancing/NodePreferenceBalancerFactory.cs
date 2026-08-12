using Grpc.Net.Client.Balancer;

namespace Kurrent.Surge.Experimental.LoadBalancing;

sealed class NodePreferenceBalancerFactory : LoadBalancerFactory {
    public const string PolicyName = "kurrent-node-preference";

    public override string Name => PolicyName;

    public override LoadBalancer Create(LoadBalancerOptions options) =>
        new NodePreferenceBalancer(options.Controller, options.LoggerFactory);
}
