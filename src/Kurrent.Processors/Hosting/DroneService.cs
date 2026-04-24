using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Hosting;

internal sealed partial class DroneService<TProcessor>(
    IDrone drone,
    ILogger<DroneService<TProcessor>> logger
) : BackgroundService
    where TProcessor : class, IRecordProcessor {
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
