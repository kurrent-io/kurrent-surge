using System.Diagnostics;
using Kurrent.Client;
using Kurrent.Client.Streams;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.KurrentDB;

public sealed partial class KurrentDBHealthCheck(string resourceName, ResourceConnectionProvider provider, ILoggerFactory loggerFactory) : IHealthCheck {
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var logger = loggerFactory.CreateLogger<KurrentDBHealthCheck>();

        try {
            LogResourcenameCheckingHealth(logger, resourceName);
            
            var connectionString = await provider
                .GetConnectionStringAsync(resourceName, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            LogResourcenameConnectionstringConnectionstring(logger, resourceName, connectionString);
            
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
                : throw new UnreachableException($"It is impossible for {resourceName} to not have records.");
        }
        catch (Exception ex) {
            return new(context.Registration.FailureStatus, exception: ex);
        }
    }

    #region ->> Logging <<-

    [LoggerMessage(LogLevel.Debug, "{ResourceName} Checking health...")]
    static partial void LogResourcenameCheckingHealth(ILogger<KurrentDBHealthCheck> logger, string resourceName);

    [LoggerMessage(LogLevel.Debug, "{ResourceName} ConnectionString: {ConnectionString}")]
    static partial void LogResourcenameConnectionstringConnectionstring(ILogger<KurrentDBHealthCheck> logger, string resourceName, string connectionString);

    #endregion
}
