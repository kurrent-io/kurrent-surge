using System.Diagnostics;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.KurrentDB;

sealed partial class KurrentDBHealthCheck(string resourceName, ResourceConnectionProvider provider, ILoggerFactory loggerFactory) : IHealthCheck {
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
                .ReadRecords(
                    new ReadRecordsOptions {
                        Start        = LogPosition.Latest,
                        Direction    = ReadDirection.Backwards,
                        Limit        = 1,
                        SkipDecoding = true
                    },
                    cancellationToken)
                .AnyAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!hasRecords)
                throw new UnreachableException($"It is impossible for {resourceName} to not have records.");

            // A node answers reads well before the schema registry can serve writes — on a cold
            // multi-node cluster the registry's own streams do not exist yet, and a client that
            // auto-registers its catalog on connect fails with an Internal error wrapping
            // StreamNotFound. Listing schemas exercises the registry subsystem, so readiness
            // means what a client actually needs rather than only that the log is reachable.
            await client.Registry
                .ListSchemas(ct: cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) {
            return new(context.Registration.FailureStatus, exception: ex);
        }
    }

    #region ->> Logging <<-

    [LoggerMessage(Microsoft.Extensions.Logging.LogLevel.Debug, "{ResourceName} Checking health...")]
    static partial void LogResourcenameCheckingHealth(ILogger<KurrentDBHealthCheck> logger, string resourceName);

    [LoggerMessage(Microsoft.Extensions.Logging.LogLevel.Debug, "{ResourceName} ConnectionString: {ConnectionString}")]
    static partial void LogResourcenameConnectionstringConnectionstring(ILogger<KurrentDBHealthCheck> logger, string resourceName, string connectionString);

    #endregion
}
