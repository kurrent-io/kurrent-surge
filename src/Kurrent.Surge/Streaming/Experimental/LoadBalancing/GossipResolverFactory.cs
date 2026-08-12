using Grpc.Net.Client.Balancer;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Creates <see cref="GossipResolver"/> instances for channels addressed with the
/// <c>kurrent</c> scheme and keeps a handle to the created resolver so leader
/// announcements can reach it.
/// </summary>
/// <remarks>
/// One factory instance serves one channel: it is registered in a private service
/// provider built per <see cref="ClusterChannel"/>.
/// </remarks>
sealed class GossipResolverFactory(GossipResolverOptions options, IGossipClient gossip) : ResolverFactory {
    public const string Scheme = "kurrent";

    public override string Name => Scheme;

    public GossipResolver? Resolver { get; private set; }

    public override Resolver Create(ResolverOptions resolverOptions) =>
        Resolver = new(options, gossip, resolverOptions.LoggerFactory);
}
