using Kurrent.Client.Processors.Gateway.Messages;

namespace Kurrent.Processors;

/// <summary>
/// Defines the interface for processing records in batches. The drone accumulates records
/// up to <see cref="MaxBatchSize"/> or until <see cref="BatchWindow"/> elapses (whichever
/// comes first), then dispatches the batch via <see cref="ProcessBatch"/>. Lifecycle hooks
/// (Connected, Checkpoint, Complete) and shutdown signals always flush the pending
/// batch before yielding.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle methods are inherited from <see cref="IProcessor"/> with default no-op implementations.
/// Only <see cref="ProcessBatch"/> must be implemented.
/// </para>
/// <para>
/// The drone calls <see cref="Kurrent.Processors.Gateway.ISubscriptionSource.Track"/> for
/// each record in a batch only after <see cref="ProcessBatch"/> returns successfully —
/// source-side checkpoint cadence is naturally aligned with batch boundaries.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRecordBatchProcessor : IProcessor {
    /// <summary>
    /// Maximum number of records in a single batch. The drone closes the batch when this
    /// many records have been buffered, even if the window has not yet expired.
    /// </summary>
    int MaxBatchSize { get; }

    /// <summary>
    /// Maximum duration to wait while gathering a batch. When this elapses with at least
    /// one buffered record, the drone yields the partial batch immediately.
    /// </summary>
    TimeSpan BatchWindow { get; }

    /// <summary>
    /// Called when a batch of records is available for processing.
    /// <remarks>
    /// This is the only method that must be implemented.
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the batch of records and cancellation token.
    /// </param>
    ValueTask ProcessBatch(BatchContext context);
}

/// <summary>
/// Context provided when a batch of records is available for processing.
/// </summary>
[PublicAPI]
public record BatchContext : DroneContext {
    public required RecordBatch   Records  { get; init; }
    public required DroneProgress Progress { get; init; }

    /// <summary>
    /// Requests that processing stop after this batch. The framework finishes auto-tracking
    /// the current batch (per the documented "track after ProcessBatch returns successfully"
    /// contract), then injects a shutdown marker. No further batches will be dispatched.
    /// </summary>
    public void StopProcessing(string? reason = null) {
        _stopRequested = true;
        _stopReason    = reason;
    }

    bool    _stopRequested;
    string? _stopReason;

    internal bool    StopRequested => _stopRequested;
    internal string? StopReason    => _stopReason;
}

/// <summary>
/// Convenience base class for batch processors. Stores <see cref="MaxBatchSize"/> and
/// <see cref="BatchWindow"/> from constructor parameters and exposes the lifecycle hooks
/// as virtual methods so derived classes can <c>override</c> them.
/// </summary>
/// <param name="maxBatchSize">Maximum records per batch.</param>
/// <param name="batchWindow">Maximum window per batch.</param>
[PublicAPI]
public abstract class RecordBatchProcessor(int maxBatchSize, TimeSpan batchWindow) : IRecordBatchProcessor {
    public int      MaxBatchSize { get; } = maxBatchSize;
    public TimeSpan BatchWindow  { get; } = batchWindow;

    public abstract ValueTask ProcessBatch(BatchContext context);

    public virtual ValueTask OnConnected(ConnectedContext context)   => ValueTask.CompletedTask;
    public virtual ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;
    public virtual ValueTask OnComplete(CompleteContext context)     => ValueTask.CompletedTask;
}
