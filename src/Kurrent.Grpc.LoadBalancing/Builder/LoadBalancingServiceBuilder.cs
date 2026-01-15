using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Builder for configuring load-balanced gRPC channels with DI.
/// </summary>
public sealed class LoadBalancingServiceBuilder {
    readonly IServiceCollection _services;
    readonly string _channelId;

    DnsEndPoint[] _seeds = [];
    bool _useTls;
    ResilienceOptions _resilience = new();
    Type? _topologySourceType;
    Type _pickingStrategyType = typeof(DefaultStrategy);
    IClusterNodePickingStrategy? _pickingStrategyInstance;
    ShouldRefreshTopology? _refreshPolicy;
    Action<GrpcChannelOptions>? _configureChannel;

    internal LoadBalancingServiceBuilder(IServiceCollection services, string channelId) {
        _services = services;
        _channelId = channelId;
    }

    /// <summary>
    /// Sets the seed endpoints for initial cluster discovery.
    /// </summary>
    /// <param name="endpoints">Endpoints in "host:port" or "host" format.</param>
    public LoadBalancingServiceBuilder WithSeeds(params string[] endpoints) {
        _seeds = endpoints.Select(e => {
            var parts = e.Split(':', 2);
            return new DnsEndPoint(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 2113);
        }).ToArray();
        return this;
    }

    /// <summary>
    /// Configures resilience settings.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    public LoadBalancingServiceBuilder WithResilience(Action<ResilienceOptions> configure) {
        configure?.Invoke(_resilience);
        return this;
    }

    /// <summary>
    /// Sets a polling-based topology source type for DI resolution.
    /// </summary>
    /// <typeparam name="TSource">The polling topology source type.</typeparam>
    public LoadBalancingServiceBuilder WithPollingTopologySource<TSource>()
        where TSource : PollingTopologySource {

        _topologySourceType = typeof(TSource);
        return this;
    }

    /// <summary>
    /// Sets a streaming topology source type for DI resolution.
    /// </summary>
    /// <typeparam name="TSource">The topology source type.</typeparam>
    public LoadBalancingServiceBuilder WithStreamingTopologySource<TSource>()
        where TSource : TopologySource {

        _topologySourceType = typeof(TSource);
        return this;
    }

    /// <summary>
    /// Configures the node picking strategy via DI.
    /// Default: <see cref="DefaultStrategy"/>.
    /// </summary>
    /// <typeparam name="TStrategy">The strategy type.</typeparam>
    public LoadBalancingServiceBuilder WithPickingStrategy<TStrategy>()
        where TStrategy : class, IClusterNodePickingStrategy {

        _pickingStrategyType = typeof(TStrategy);
        _pickingStrategyInstance = null;
        return this;
    }

    /// <summary>
    /// Configures the node picking strategy with an instance.
    /// </summary>
    /// <param name="strategy">The strategy instance.</param>
    public LoadBalancingServiceBuilder WithPickingStrategy(IClusterNodePickingStrategy strategy) {
        _pickingStrategyInstance = strategy ?? throw new ArgumentNullException(nameof(strategy));
        return this;
    }

    /// <summary>
    /// Sets the refresh policy for error-triggered topology refresh.
    /// </summary>
    /// <param name="policy">The refresh policy.</param>
    public LoadBalancingServiceBuilder WithRefreshPolicy(ShouldRefreshTopology policy) {
        _refreshPolicy = policy;
        return this;
    }

    /// <summary>
    /// Configures additional gRPC channel options.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    public LoadBalancingServiceBuilder ConfigureChannel(Action<GrpcChannelOptions> configure) {
        _configureChannel = configure;
        return this;
    }

    /// <summary>
    /// Enables or disables TLS for connections.
    /// </summary>
    /// <param name="useTls">True to use TLS.</param>
    public LoadBalancingServiceBuilder UseTls(bool useTls = true) {
        _useTls = useTls;
        return this;
    }

    /// <summary>
    /// Builds and registers the services in the DI container.
    /// </summary>
    public IServiceCollection Build() {
        if (_topologySourceType == null)
            throw new InvalidOperationException("A topology source type is required");

        _services.AddSingleton(_topologySourceType);

        // Register picking strategy
        if (_pickingStrategyInstance != null) {
            _services.AddSingleton(_pickingStrategyInstance);
        }
        else {
            _services.AddSingleton(typeof(IClusterNodePickingStrategy), _pickingStrategyType);
        }

        _services.AddSingleton<ResolverFactory>(sp => {
            var source = (TopologySource)sp.GetRequiredService(_topologySourceType);
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new ClusterResolverFactory(source, _seeds, _resilience, _useTls, loggerFactory);
        });

        _services.AddSingleton<LoadBalancerFactory>(sp => {
            var strategy = sp.GetRequiredService<IClusterNodePickingStrategy>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return new ClusterLoadBalancerFactory(strategy, loggerFactory);
        });

        var seeds = _seeds;
        var useTls = _useTls;
        var configureChannel = _configureChannel;
        var channelId = _channelId;

        _services.AddSingleton(sp => {
            var options = new GrpcChannelOptions {
                ServiceProvider = sp,
                LoggerFactory = sp.GetService<ILoggerFactory>(),
                Credentials = useTls ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig {
                    LoadBalancingConfigs = { new LoadBalancingConfig("cluster") }
                }
            };

            configureChannel?.Invoke(options);
            return GrpcChannel.ForAddress($"cluster:///{channelId}", options);
        });

        return _services;
    }
}
