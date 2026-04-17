using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Options;

/// <summary>
/// Generic hosted service that runs a Kurrent processor.
/// Automatically registered when using <see cref="KurrentProcessorServiceCollectionExtensions.AddKurrentProcessor{TProcessor}"/>.
/// </summary>
/// <typeparam name="TProcessor">The processor type to host.</typeparam>
internal sealed partial class KurrentProcessorHostedService<TProcessor>(
    IDrone drone,
    ILogger<KurrentProcessorHostedService<TProcessor>> logger
) : BackgroundService
    where TProcessor : class, IRecordProcessor {

    /// <summary>
    /// Gets the underlying processor drone. Internal for testing purposes.
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting processor {ProcessorId} drone {DroneId}")]
    partial void LogStarting(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} drone {DroneId} stopped")]
    partial void LogStopped(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor {ProcessorId} drone {DroneId} failed")]
    partial void LogFailed(Exception ex, string processorId, string droneId);
}
