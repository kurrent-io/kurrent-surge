using Kurrent.Surge.Processors.Configuration;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Represents a running processor drone instance.
/// </summary>
[PublicAPI]
public interface IDrone {
    /// <summary>
    /// Configuration options for the processor drone. Provides access to various
    /// settings and parameters such as processor and drone identifiers, buffer capacity,
    /// debugging features, parallelism, and advanced subscription or checkpointing behaviors.
    /// </summary>
    ProcessorOptions Options { get; }
    
    /// <summary>
    /// Current lifecycle status of the processor drone.
    /// </summary>
    DroneStatus Status { get; }
    
    /// <summary>
    /// Unique identifier of the processor being executed.
    /// </summary>
    string ProcessorId => Options.ProcessorId;

    /// <summary>
    /// The unique identifier of this drone instance.
    /// Could have been automatically generated if not provided in settings.
    /// </summary>
    string DroneId => Options.DroneId;
    
    /// <summary>
    /// Runs the processor drone asynchronously until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Token to request cancellation of processing.</param>
    Task RunAsync(CancellationToken stoppingToken);
}

/// <summary>
/// Defines the lifecycle status of a processor drone.
/// </summary>
/// <remarks>
/// Values match the original Surge <c>ProcessorState</c> ordinal for ordinal, so the two are
/// interchangeable on the wire: <c>Activating</c> is <see cref="Starting"/>, <c>Running</c> is
/// <see cref="Processing"/>, <c>Deactivating</c> is <see cref="Stopping"/>. Like Surge, a failed run
/// is <see cref="Stopped"/> carrying an error rather than a status of its own.
/// </remarks>
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
    /// Shutting down: the final barrier has run and the drone is releasing its resources.
    /// </summary>
    Stopping = 3,

    /// <summary>
    /// Terminal status, whether the run ended cleanly, by cancellation, or on an error. Read
    /// <see cref="ProcessorStateChanged.Error"/> on the transition to tell them apart.
    /// </summary>
    Stopped = 4
}
