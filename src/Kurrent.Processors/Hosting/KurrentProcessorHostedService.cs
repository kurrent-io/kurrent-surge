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
        LogStarting(drone.ProcessorId, drone.WorkerId);

        try {
            await drone.RunAsync(stoppingToken).ConfigureAwait(false);
            LogStopped(drone.ProcessorId, drone.WorkerId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            LogStopped(drone.ProcessorId, drone.WorkerId);
        }
        catch (Exception ex) {
            LogFailed(ex, drone.ProcessorId, drone.WorkerId);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting processor {ProcessorId} drone {WorkerId}")]
    partial void LogStarting(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} drone {WorkerId} stopped")]
    partial void LogStopped(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor {ProcessorId} drone {WorkerId} failed")]
    partial void LogFailed(Exception ex, string processorId, string workerId);
}
