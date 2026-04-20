using Microsoft.Extensions.Hosting;

namespace Kurrent.Processors.Hosting;

/// <summary>
/// Hosts a single processor drone. Registered by
/// <see cref="KurrentProcessorServiceCollectionExtensions.AddKurrentProcessor{TProcessor}"/>.
/// </summary>
/// <remarks>
/// <para>
/// One service instance per processor type. The drone is constructed eagerly in the
/// DI factory — connection resolution and validation errors surface at host startup
/// rather than mid-run.
/// </para>
/// <para>
/// If <see cref="BackgroundService.ExecuteAsync"/> throws, the default
/// <see cref="HostOptions.BackgroundServiceExceptionBehavior"/> on .NET 6+ is
/// <c>StopHost</c>. A single processor failure will tear down the entire host.
/// Configure <c>HostOptions</c> if a different behavior is required for multi-processor apps.
/// </para>
/// </remarks>
/// <typeparam name="TProcessor">The processor type hosted by this service.</typeparam>
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
