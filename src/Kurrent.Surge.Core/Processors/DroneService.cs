using Microsoft.Extensions.Hosting;

namespace Kurrent.Surge.Processors.Hosting;

sealed partial class DroneService<TProcessor>(
    IDrone drone,
    ILogger<DroneService<TProcessor>> logger
) : BackgroundService
    where TProcessor : class, IProcessor {
    /// <summary>
    /// The drone this service hosts. Internal for testing purposes.
    /// </summary>
    internal IDrone Drone => drone;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LogStarting(drone.ProcessorId, drone.DroneId);

        try {
            await drone.RunAsync(stoppingToken).ConfigureAwait(false);
            LogStopped(drone.ProcessorId, drone.DroneId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            LogStopped(drone.ProcessorId, drone.DroneId);
        }
        catch (Exception ex) {
            LogFailed(ex, drone.ProcessorId, drone.DroneId);

            // A faulted BackgroundService stops a .NET 10 host with exit code 0. This mirrors .NET 11:
            // https://learn.microsoft.com/dotnet/core/compatibility/extensions/11/ihost-runasync-stopasync-throw-backgroundservice-failure
            Environment.ExitCode = 1;
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hosting drone {DroneId} for processor {ProcessorId}")]
    partial void LogStarting(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Host released drone {DroneId} for processor {ProcessorId}")]
    partial void LogStopped(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Host observed fatal failure in drone {DroneId} for processor {ProcessorId}")]
    partial void LogFailed(Exception ex, string processorId, string droneId);
}
