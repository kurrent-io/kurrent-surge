namespace Kurrent.Surge.Processors;

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
    /// Called once at startup, before the checkpoint is resolved and before the subscription opens.
    /// <remarks>
    /// Use this for one-time initialization that must complete before any position is decided —
    /// opening a connection, creating state tables, priming caches. A processor that keeps its own
    /// checkpoints resolves them here and calls <see cref="InitializeContext.OwnCheckpoints"/>.
    /// Failures propagate as fatal startup errors.
    /// </remarks>
    /// </summary>
    ValueTask OnInitialize(InitializeContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called once when connected to the server, before any records are dispatched.
    /// <remarks>
    /// The subscription is live by this point and its start position is already settled — anything
    /// that must run earlier belongs in <see cref="OnInitialize"/>.
    /// </remarks>
    /// </summary>
    ValueTask OnConnected(ConnectedContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a checkpoint is signaled to allow for pre-checkpoint actions like flushing state.
    /// </summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;
}
