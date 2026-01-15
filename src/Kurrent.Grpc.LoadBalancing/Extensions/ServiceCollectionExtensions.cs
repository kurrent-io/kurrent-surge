using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Extension methods for registering cluster load balancing with DI.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds gRPC load balancing services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="channelId">Unique identifier for the channel.</param>
    public static LoadBalancingServiceBuilder AddGrpcLoadBalancing(
        this IServiceCollection services,
        string channelId) {

        return new LoadBalancingServiceBuilder(services, channelId);
    }

    /// <summary>
    /// Adds gRPC load balancing services with configuration binding.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="channelId">Unique identifier for the channel.</param>
    /// <param name="configuration">Configuration section with load balancing settings.</param>
    public static LoadBalancingServiceBuilder AddGrpcLoadBalancing(
        this IServiceCollection services,
        string channelId,
        IConfiguration configuration) {

        var options = configuration.Get<LoadBalancingOptions>() ?? new();

        return new LoadBalancingServiceBuilder(services, channelId)
            .WithSeeds(options.Seeds)
            .UseTls(options.UseTls)
            .WithResilience(r => {
                r.Timeout = options.Resilience.Timeout;
                r.MaxDiscoveryAttempts = options.Resilience.MaxDiscoveryAttempts;
                r.InitialBackoff = options.Resilience.InitialBackoff;
                r.MaxBackoff = options.Resilience.MaxBackoff;
                r.RefreshOnStatusCodes = options.Resilience.RefreshOnStatusCodes;
            });
    }
}
