using Kurrent.Processors.Options;

namespace Kurrent.Processors;

/// <summary>
/// Represents a running processor drone instance.
/// </summary>
[PublicAPI]
public interface IDrone {
    /// <summary>
    /// Unique identifier of the processor being executed.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// The unique identifier of this drone instance.
    /// Could have been automatically generated if not provided in settings.
    /// </summary>
    string DroneId { get; }

    /// <summary>
    /// Current lifecycle status of the processor drone.
    /// </summary>
    DroneStatus Status { get; }

    /// <summary>
    /// The configuration settings used by this processor drone.
    /// </summary>
    ProcessorOptions Settings { get; }

    /// <summary>
    /// Runs the processor drone asynchronously until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Token to request cancellation of processing.</param>
    Task RunAsync(CancellationToken stoppingToken);
}

/// <summary>
/// Defines the lifecycle status of a processor drone.
/// </summary>
[PublicAPI]
public enum DroneStatus {
    /// <summary>
    /// Fallback value for serialization and protocol compatibility.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Drone is bootstrapping: connecting, restoring state, and preparing to process.
    /// </summary>
    Starting = 1,

    /// <summary>
    /// Actively processing records.
    /// </summary>
    Processing = 2,

    /// <summary>
    /// Terminal status for a clean shutdown or cancellation.
    /// </summary>
    Stopped = 3,

    /// <summary>
    /// Terminal status for an unrecoverable error.
    /// </summary>
    Error = 4
}
