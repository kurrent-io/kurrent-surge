using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Streaming.Pipeline;

public record SinkCheckpointContext {
    public required CheckpointId      CheckpointId { get; init; }
    public required CheckpointTrigger Trigger      { get; init; }
    public required DateTimeOffset    Timestamp    { get; init; }
}

public record CommitContext {
    public required CheckpointId      CheckpointId { get; init; }
    public required CheckpointTrigger Trigger      { get; init; }
    public required DateTimeOffset    Timestamp    { get; init; }
}

public record AbortContext {
    public required CheckpointId      CheckpointId { get; init; }
    public required CheckpointTrigger Trigger      { get; init; }
    public required DateTimeOffset    Timestamp    { get; init; }
}

/// <summary>
/// Defines a unified, lifecycle-aware data destination component capable of 
/// processing data streams and participating in distributed 2-Phase Commit (2PC) checkpoint protocols.
/// </summary>
public interface ISink {
    /// <summary>
    /// Writes a single data payload asynchronously to the sink's internal buffer or target destination.
    /// </summary>
    /// <param name="data">The data object to be processed and emitted by the sink.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous write operation.</returns>
    /// <remarks>
    /// This method is invoked by the framework execution engine continuously as data flows through the stream.
    /// To maximize throughput, implementations should use lightweight in-memory buffering here 
    /// rather than executing costly external IO operations for every individual call.
    /// </remarks>
    ValueTask Write(object data, CancellationToken ct = default);

    /// <summary>
    /// Acts as the Phase 1 (Prepare) phase of the checkpoint protocol. Forces the sink to flush 
    /// its internal memory buffers and stage its current data state securely.
    /// </summary>
    /// <param name="context">The engine-provided metadata context containing details about the active checkpoint barrier.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous flushing and checkpoint preparation.</returns>
    /// <remarks>
    /// Simple sinks should treat this method purely as a traditional "Flush" command to guarantee data is pushed out to disk or the network.
    /// Advanced/Transactional sinks must use this step to pre-commit or stage their operations (e.g., executing a database PREPARE TRANSACTION statement) 
    /// so they can guarantee success in the subsequent commit phase.
    /// </remarks>
    ValueTask OnCheckpoint(SinkCheckpointContext context, CancellationToken ct = default);

    /// <summary>
    /// Acts as the Phase 2 (Commit) phase of the checkpoint protocol. Formally finalizes and completes 
    /// the staged transaction associated with the specified checkpoint.
    /// </summary>
    /// <param name="checkpointId">The unique identifier of the checkpoint to permanently commit.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous transaction completion.</returns>
    /// <remarks>
    /// This method is only triggered by the framework engine once all system components have successfully acknowledged the matching <see cref="OnCheckpoint"/> phase.
    /// Non-transactional (simple) sinks can leave this implementation empty or return <see cref="ValueTask.CompletedTask"/>.
    /// </remarks>
    ValueTask OnCheckpointCommit(CheckpointId checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Handles the rollback or cancellation of a staged checkpoint when a failure or abort condition occurs elsewhere in the pipeline.
    /// </summary>
    /// <param name="checkpointId">The unique identifier of the checkpoint being canceled.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous abort or cleanup execution.</returns>
    /// <remarks>
    /// This method is invoked if any parallel sink fails during its <see cref="OnCheckpoint"/> step, or if the global transaction is rejected by the engine.
    /// Transactional sinks must release active database locks, drop temporary tables, or roll back staged uncommitted changes here.
    /// Simple sinks can use this hook to clear or purge corrupted internal memory buffers to reset their stream state safely.
    /// </remarks>
    ValueTask OnCheckpointAbort(CheckpointId checkpointId, CancellationToken ct = default);
}

/// <summary>
/// Provides a type-safe abstract base class for implementing lifecycle-aware sinks. 
/// Bridges the non-generic framework runtime interface (<see cref="ISink"/>) with strongly-typed data processing.
/// </summary>
/// <typeparam name="T">The specific data type that this sink is designed to consume and process.</typeparam>
public abstract class DataSink<T> : ISink {
    /// <summary>
    /// Writes a single strongly-typed data payload asynchronously to the sink's internal buffer or target destination.
    /// </summary>
    /// <param name="data">The strongly-typed data object to be processed and emitted by the sink.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous write operation.</returns>
    /// <remarks>
    /// This method is only invoked when the incoming framework payload matches the generic type parameter <typeparamref name="T"/>.
    /// Implementations should utilize fast, non-blocking operations or memory buffering here to preserve framework throughput.
    /// </remarks>
    public abstract ValueTask Write(T data, CancellationToken ct = default);

    /// <summary>
    /// Acts as the Phase 1 (Prepare) phase of the checkpoint protocol. Forces the sink to flush 
    /// its strongly-typed internal memory buffers and stage its current data state securely.
    /// </summary>
    /// <param name="context">The engine-provided metadata context containing details about the active checkpoint barrier.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous flushing and checkpoint preparation.</returns>
    public abstract ValueTask OnCheckpoint(SinkCheckpointContext context, CancellationToken ct = default);

    /// <summary>
    /// Acts as the Phase 2 (Commit) phase of the checkpoint protocol. Formally finalizes and completes 
    /// the staged transaction associated with the specified checkpoint.
    /// </summary>
    /// <param name="checkpointId">The unique identifier of the checkpoint to permanently commit.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous transaction completion.</returns>
    /// <remarks>
    /// The default implementation is a no-op that returns <see cref="ValueTask.CompletedTask"/>. 
    /// Advanced transactional sinks should override this method to finalize their operations.
    /// </remarks>
    public virtual ValueTask OnCheckpointCommit(CheckpointId checkpointId, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Handles the rollback or cancellation of a staged checkpoint when a failure or abort condition occurs elsewhere in the pipeline.
    /// </summary>
    /// <param name="checkpointId">The unique identifier of the checkpoint being canceled.</param>
    /// <param name="ct">A token to monitor for cancellation requests during execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous abort or cleanup execution.</returns>
    /// <remarks>
    /// The default implementation is a no-op that returns <see cref="ValueTask.CompletedTask"/>.
    /// Override this method to release active database locks, clear corrupted buffers, or roll back staged uncommitted changes.
    /// </remarks>
    public virtual ValueTask OnCheckpointAbort(CheckpointId checkpointId, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Explicit implementation of the non-generic <see cref="ISink.Write"/> method used exclusively by the framework engine.
    /// Safely inspects and routes the incoming untyped object data to the strongly-typed <see cref="Write(T, CancellationToken)"/> method.
    /// </summary>
    ValueTask ISink.Write(object data, CancellationToken ct) 
    {
        if (data is T typedData)
            return Write(typedData, ct);

        // Handle unexpected types gracefully or ignore
        return ValueTask.CompletedTask;
    }
}