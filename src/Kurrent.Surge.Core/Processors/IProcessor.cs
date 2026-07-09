namespace Kurrent.Surge.Streaming;

/// <summary>
/// Common base interface for record-processing primitives. Carries the shared lifecycle
/// hooks that fire regardless of whether the processor consumes records one at a time
/// (<see cref="IRecordProcessor"/>) or in batches (<see cref="IRecordBatchProcessor"/>).
/// </summary>
/// <remarks>
/// Implementers should normally derive from <see cref="IRecordProcessor"/> or
/// <see cref="IRecordBatchProcessor"/> rather than implementing this interface directly.
/// All lifecycle methods have default no-op implementations.
/// </remarks>
[PublicAPI]
public interface IProcessor {
    /// <summary>
    /// Called once when connected to the server, before any records are dispatched.
    /// <remarks>
    /// Use this for one-time initialization such as creating state tables, opening external
    /// resources, or priming caches. Failures propagate as fatal startup errors.
    /// </remarks>
    /// </summary>
    ValueTask OnConnected(ConnectedContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a checkpoint is signaled to allow for pre-checkpoint actions like flushing state.
    /// </summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;
}
