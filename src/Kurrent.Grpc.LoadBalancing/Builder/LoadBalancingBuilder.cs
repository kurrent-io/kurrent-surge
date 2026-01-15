using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Builder for configuring load-balanced gRPC channels without DI.
/// </summary>
public sealed class LoadBalancingBuilder {
    DnsEndPoint[] _seeds = [];
    bool _useTls;
    ResilienceOptions _resilience = new();
    TopologySource? _topologySource;
    IClusterNodePickingStrategy _pickingStrategy = DefaultStrategy.Instance;
    ShouldRefreshTopology? _refreshPolicy;
    ILoggerFactory? _loggerFactory;
    Action<GrpcChannelOptions>? _configureChannel;

    /// <summary>
    /// Sets the seed endpoints for initial cluster discovery.
    /// </summary>
    /// <param name="endpoints">Endpoints in "host:port" or "host" format.</param>
    public LoadBalancingBuilder WithSeeds(params string[] endpoints) {
        _seeds = endpoints.Select(ParseEndpoint).ToArray();
        return this;
    }

    /// <summary>
    /// Sets the seed endpoints for initial cluster discovery.
    /// </summary>
    /// <param name="endpoints">DNS endpoints.</param>
    public LoadBalancingBuilder WithSeeds(params DnsEndPoint[] endpoints) {
        _seeds = endpoints;
        return this;
    }

    /// <summary>
    /// Configures resilience settings.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    public LoadBalancingBuilder WithResilience(Action<ResilienceOptions> configure) {
        configure?.Invoke(_resilience);
        return this;
    }

    /// <summary>
    /// Sets a polling-based topology source.
    /// </summary>
    /// <param name="source">The polling topology source.</param>
    public LoadBalancingBuilder WithPollingTopologySource(PollingTopologySource source) {
        _topologySource = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    /// <summary>
    /// Sets a streaming topology source.
    /// </summary>
    /// <param name="source">The topology source.</param>
    public LoadBalancingBuilder WithStreamingTopologySource(TopologySource source) {
        _topologySource = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    /// <summary>
    /// Configures the node picking strategy.
    /// Default: <see cref="DefaultStrategy"/> (writes to leader, reads round-robin).
    /// </summary>
    /// <param name="strategy">The picking strategy.</param>
    public LoadBalancingBuilder WithPickingStrategy(IClusterNodePickingStrategy strategy) {
        _pickingStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    /// <summary>
    /// Sets the refresh policy for error-triggered topology refresh.
    /// </summary>
    /// <param name="policy">The refresh policy.</param>
    public LoadBalancingBuilder WithRefreshPolicy(ShouldRefreshTopology policy) {
        _refreshPolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    /// <summary>
    /// Sets the logger factory for logging.
    /// </summary>
    /// <param name="factory">The logger factory.</param>
    public LoadBalancingBuilder WithLoggerFactory(ILoggerFactory? factory) {
        _loggerFactory = factory;
        return this;
    }

    /// <summary>
    /// Configures additional gRPC channel options.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    public LoadBalancingBuilder ConfigureChannel(Action<GrpcChannelOptions> configure) {
        _configureChannel = configure;
        return this;
    }

    /// <summary>
    /// Enables or disables TLS for connections.
    /// </summary>
    /// <param name="useTls">True to use TLS.</param>
    public LoadBalancingBuilder UseTls(bool useTls = true) {
        _useTls = useTls;
        return this;
    }

    /// <summary>
    /// Builds the load-balanced gRPC channel.
    /// </summary>
    /// <param name="address">
    /// Optional logical cluster address (e.g., "cluster:///my-cluster").
    /// If not provided, a random identifier is generated.
    /// </param>
    public GrpcChannel Build(string? address = null) {
        if (_seeds.Length == 0)
            throw new InvalidOperationException("At least one seed endpoint is required");

        if (_topologySource == null)
            throw new InvalidOperationException("A topology source is required");

        var refreshPolicy = _refreshPolicy
            ?? RefreshPolicy.FromStatusCodeInts(_resilience.RefreshOnStatusCodes);

        var services = new ServiceCollection();

        services.AddSingleton<ResolverFactory>(sp =>
            new ClusterResolverFactory(_topologySource, _seeds, _resilience, _useTls, _loggerFactory));

        services.AddSingleton<LoadBalancerFactory>(sp =>
            new ClusterLoadBalancerFactory(_pickingStrategy, _loggerFactory));

        var serviceProvider = services.BuildServiceProvider();

        var options = new GrpcChannelOptions {
            ServiceProvider = serviceProvider,
            LoggerFactory = _loggerFactory,
            Credentials = _useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig {
                LoadBalancingConfigs = { new LoadBalancingConfig("cluster") }
            }
        };

        _configureChannel?.Invoke(options);

        // Use provided address or generate a random channel identifier
        var channelAddress = address ?? $"cluster:///{Guid.NewGuid().ToString("N")[..8]}";
        return GrpcChannel.ForAddress(channelAddress, options);
    }

    static DnsEndPoint ParseEndpoint(string endpoint) {
        var parts = endpoint.Split(':', 2);
        var host = parts[0];
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 2113;
        return new DnsEndPoint(host, port);
    }
}
