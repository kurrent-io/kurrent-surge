using Kurrent.Client;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Kurrent.Sdk.Testkit.Hosting;

public sealed class KurrentDBHealthCheckV2(string resourceName, ResourceConnectionSubscriber provider, ILoggerFactory loggerFactory) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var logger = loggerFactory.CreateLogger<KurrentDBHealthCheckV2>();

        try {
            logger.LogWarning("Checking health for KurrentDB resource: {ResourceName}", resourceName);
            
            var connectionString = await provider
                .GetConnectionStringAsync(resourceName)
                .ConfigureAwait(false);

            logger.LogWarning("Using ConnectionString: {ConnectionString}", connectionString);
            
            await using var client = KurrentClient.Create(connectionString, opts => {
                opts.ConnectionName = resourceName;
                opts.LoggerFactory  = loggerFactory;
                // opts.Resilience     = KurrentClientResilienceOptions.FailFast;
                opts.Resilience     = KurrentClientResilienceOptions.NoResilience;
            });

            var hasRecords = await client.Streams
                .ReadRecords(req => req.FromLatestBackwards().Limit(1).SkipDecoding(), cancellationToken)
                .AnyAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return hasRecords
                ? HealthCheckResult.Healthy()
                : new(context.Registration.FailureStatus, "Failed to read from KurrentDB");
        }
        catch (Exception ex) {
            logger.LogError(ex, "Failed to check health for KurrentDB resource: {ResourceName}", resourceName);

            return new(context.Registration.FailureStatus, exception: ex);
        }
    }
}

public sealed class KurrentDBHealthCheck(string resourceName, ResourceConnectionProvider<KurrentDBResource> provider, ILoggerFactory loggerFactory) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var logger = loggerFactory.CreateLogger<KurrentDBHealthCheck>();

        try {
            var connectionString = await provider
                .GetConnectionStringAsync(resourceName, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
            
            logger.LogWarning("Using ConnectionString: {ConnectionString}", connectionString);

            await using var client = KurrentClient.Create(connectionString, opts => {
                opts.ConnectionName = resourceName;
                opts.LoggerFactory  = loggerFactory;
                opts.Resilience     = KurrentClientResilienceOptions.NoResilience;
            });

            var hasRecords = await client.Streams
                .ReadRecords(req => req.FromLatestBackwards().Limit(1).SkipDecoding(), cancellationToken)
                .AnyAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return hasRecords
                ? HealthCheckResult.Healthy()
                : new(context.Registration.FailureStatus, "Failed to read from KurrentDB.");
        }
        catch (Exception exception) {
            return new(context.Registration.FailureStatus, exception: exception);
        }
    }
}
