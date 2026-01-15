using System.Net;
using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Factory for creating ClusterResolver instances.
/// </summary>
internal sealed class ClusterResolverFactory : ResolverFactory {
    readonly TopologySource _source;
    readonly DnsEndPoint[] _seeds;
    readonly ResilienceOptions _resilience;
    readonly bool _useTls;
    readonly ILoggerFactory? _loggerFactory;

    public override string Name => "cluster";

    public ClusterResolverFactory(
        TopologySource source,
        DnsEndPoint[] seeds,
        ResilienceOptions resilience,
        bool useTls = false,
        ILoggerFactory? loggerFactory = null) {

        _source = source;
        _seeds = seeds;
        _resilience = resilience;
        _useTls = useTls;
        _loggerFactory = loggerFactory;
    }

    public override Resolver Create(ResolverOptions options) =>
        new ClusterResolver(_source, _seeds, _resilience, _useTls, _loggerFactory);
}
