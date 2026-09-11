using System.Runtime.CompilerServices;
using Kurrent.Surge.Processors;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kurrent.Surge;

/// <summary>
/// Reports the hosted processor's health from the last lifecycle transition it observed.
/// </summary>
sealed class ProcessorHealthCheck<TProcessor> : IHealthCheck, IProcessorObserver where TProcessor : class, IProcessor {
    volatile StrongBox<ProcessorStateChanged>? _latest;

    public void OnStateChanged(in ProcessorStateChanged evt) => _latest = new(evt);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        if (_latest is not { Value: var evt })
            return Task.FromResult(HealthCheckResult.Degraded("Processor has not started"));

        var data = new Dictionary<string, object> {
            ["processorId"]             = evt.Info.ProcessorId.ToString(),
            ["droneId"]                 = evt.Info.DroneId,
            ["status"]                  = evt.ToStatus.ToString(),
            ["totalRecords"]            = evt.Progress.TotalRecords,
            ["recordsPerSecond"]        = evt.Progress.RecordsPerSecond,
            ["lowestCommittedPosition"] = evt.Progress.LowestCommittedPosition.ToString()
        };

        if (evt.Progress.LastProcessedAt is { } lastProcessedAt)
            data["lastProcessedAt"] = lastProcessedAt;

        var result = evt switch {
            { Error: { } error }                                      => HealthCheckResult.Unhealthy("Processor failed", error, data),
            { ToStatus: DroneStatus.Processing }                      => HealthCheckResult.Healthy("Processor is processing", data),
            { ToStatus: DroneStatus.Stopping or DroneStatus.Stopped } => HealthCheckResult.Unhealthy($"Processor is {evt.ToStatus}", data: data),
            _                                                         => HealthCheckResult.Degraded("Processor is starting", data: data)
        };

        return Task.FromResult(result);
    }
}
