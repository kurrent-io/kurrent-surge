using Grpc.Net.Client.Balancer;

namespace Kurrent.Surge.Client.LoadBalancing;

/// <summary>
/// Creates <see cref="KurrentDBLoadBalancer"/> instances and keeps a handle to the
/// created balancer so the connected endpoint can be read back for <see cref="ClusterChannel.Target"/>.
/// </summary>
/// <remarks>
/// One factory instance serves one channel: it is registered in a private service
/// provider built per <see cref="ClusterChannel"/>.
/// </remarks>
sealed class KurrentDBLoadBalancerFactory : LoadBalancerFactory {
    public const string PolicyName = "kurrent-node-preference";

    public override string Name => PolicyName;

    public KurrentDBLoadBalancer? Balancer { get; private set; }

    public override LoadBalancer Create(LoadBalancerOptions options) =>
        Balancer = new(options.Controller, options.LoggerFactory);
}
