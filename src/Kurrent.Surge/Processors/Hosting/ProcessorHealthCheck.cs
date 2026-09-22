using System.Runtime.CompilerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kurrent.Surge.Processors.Hosting;

/// <summary>
/// Reports the hosted processor as healthy, degraded, or unhealthy from its lifecycle and checkpoint events.
/// </summary>
sealed class ProcessorHealthCheck : IHealthCheck, IProcessorObserver {
    volatile StrongBox<ProcessorStateChanged>? _latest;
    volatile Exception?                        _checkpointFailure;

    public void OnStateChanged(in ProcessorStateChanged evt) => _latest = new(evt);

    public void OnCheckpointFailed(in CheckpointFailed evt) => _checkpointFailure = evt.Error;

    public void OnCheckpointCommitted(in CheckpointCommitted evt) => _checkpointFailure = null;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
        var checkpointFailure = _checkpointFailure;

        var result = _latest?.Value switch {
            null                                                                    => HealthCheckResult.Degraded("Processor has not started"),
            { Error: { } error }                                                    => HealthCheckResult.Unhealthy("Processor failed", error),
            { ToStatus: (DroneStatus.Stopping or DroneStatus.Stopped) and var status } => HealthCheckResult.Unhealthy($"Processor is {status}"),
            { ToStatus: DroneStatus.Processing } when checkpointFailure is not null => HealthCheckResult.Degraded("Checkpoint failed", checkpointFailure),
            { ToStatus: DroneStatus.Processing }                                    => HealthCheckResult.Healthy("Processor is processing"),
            _                                                                       => HealthCheckResult.Degraded("Processor is starting")
        };

        return Task.FromResult(result);
    }
}
