using Microsoft.Extensions.Hosting;

namespace Kurrent.Streaming.Hosting;

internal sealed partial class DroneBatchService<TProcessor>(
    IDrone drone,
    ILogger<DroneBatchService<TProcessor>> logger
) : BackgroundService
    where TProcessor : class, IRecordBatchProcessor {
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hosting drone {DroneId} for batch processor {ProcessorId}")]
    partial void LogStarting(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Host released drone {DroneId} for batch processor {ProcessorId}")]
    partial void LogStopped(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Host observed fatal failure in drone {DroneId} for batch processor {ProcessorId}")]
    partial void LogFailed(Exception ex, string processorId, string droneId);
}
