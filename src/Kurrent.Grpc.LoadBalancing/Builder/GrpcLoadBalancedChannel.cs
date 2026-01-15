using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Factory methods for creating load-balanced gRPC channels.
/// </summary>
public static class GrpcLoadBalancedChannel {
    /// <summary>
    /// Creates a load-balanced gRPC channel.
    /// </summary>
    /// <param name="address">
    /// Logical cluster address (e.g., "cluster:///my-cluster").
    /// The path component is used as the channel identifier.
    /// </param>
    /// <param name="configure">Builder configuration action.</param>
    public static GrpcChannel ForAddress(string address, Action<LoadBalancingBuilder> configure) {
        var builder = new LoadBalancingBuilder();
        configure(builder);
        return builder.Build(address);
    }

    /// <summary>
    /// Creates a load-balanced gRPC channel with auto-generated identifier.
    /// </summary>
    /// <param name="configure">Builder configuration action.</param>
    public static GrpcChannel Create(Action<LoadBalancingBuilder> configure) {
        var builder = new LoadBalancingBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Creates a load-balanced gRPC channel from configuration.
    /// </summary>
    /// <param name="configuration">Configuration section with load balancing settings.</param>
    /// <param name="configure">Optional additional configuration.</param>
    public static GrpcChannel FromConfiguration(
        IConfiguration configuration,
        Action<LoadBalancingBuilder>? configure = null) {

        var options = configuration.Get<LoadBalancingOptions>() ?? new();

        var builder = new LoadBalancingBuilder()
            .WithSeeds(options.Seeds)
            .UseTls(options.UseTls)
            .WithResilience(r => {
                r.Timeout = options.Resilience.Timeout;
                r.MaxDiscoveryAttempts = options.Resilience.MaxDiscoveryAttempts;
                r.InitialBackoff = options.Resilience.InitialBackoff;
                r.MaxBackoff = options.Resilience.MaxBackoff;
                r.RefreshOnStatusCodes = options.Resilience.RefreshOnStatusCodes;
            });

        configure?.Invoke(builder);
        return builder.Build();
    }
}
