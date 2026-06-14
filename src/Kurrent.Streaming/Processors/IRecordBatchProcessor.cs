using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming;

/// <summary>
/// Defines the interface for processing records in batches. The drone accumulates records
/// up to <see cref="MaxBatchSize"/> or until <see cref="BatchWindow"/> elapses (whichever
/// comes first), then dispatches the batch via <see cref="ProcessBatch"/>. Lifecycle hooks
/// (Connected, Checkpoint) and shutdown signals always flush the pending batch before yielding.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle methods are inherited from <see cref="IProcessor"/> with default no-op implementations.
/// Only <see cref="ProcessBatch"/> must be implemented.
/// </para>
/// <para>
/// The drone tracks positions for
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
/// Context provided when a batch of records is available for processing. Manual tracking and
/// checkpointing are batch-granular: <see cref="Track()"/> claims the whole batch,
/// <see cref="Track(RecordPosition)"/> a prefix, and <see cref="Checkpoint(string)"/> requests a
/// commit barrier after the batch.
/// </summary>
[PublicAPI]
public record BatchContext : DroneContext {
    public BatchContext() { }

    public BatchContext(Action<RecordPosition> requestTrack) =>
        RequestTrack = requestTrack;

    public required RecordBatch   Records  { get; init; }
    public required DroneProgress Progress { get; init; }

    internal Action<RecordPosition> RequestTrack { get; init; } = static _ => { };

    /// <summary>
    /// Marks the whole batch as durably handled — tracks every record's position. Composes
    /// idempotently with auto-track; the tracker keeps the latest position per partition.
    /// </summary>
    public void Track() {
        _trackedByUser = true;
        foreach (var record in Records)
            RequestTrack(record.Position);
    }

    /// <summary>
    /// Marks the batch handled up to and including <paramref name="upTo"/> — a prefix. Records past
    /// it stay pending. The tracker is strictly monotonic, so positions at or below the last
    /// committed position are skipped.
    /// </summary>
    /// <param name="upTo">The last position to claim; records beyond it remain untracked.</param>
    public void Track(RecordPosition upTo) {
        _trackedByUser = true;
        foreach (var record in Records) {
            if (record.Position <= upTo)
                RequestTrack(record.Position);
        }
    }

    /// <summary>
    /// Requests a manual checkpoint barrier after this batch. The framework raises
    /// <c>OnCheckpoint</c> with <see cref="CheckpointTrigger.Manual"/> and the supplied
    /// <paramref name="reason"/>; positions are taken from the tracker's state at the moment the
    /// framework dispatches the marker. At most one manual checkpoint may be requested per
    /// <c>ProcessBatch</c> invocation.
    /// </summary>
    /// <param name="reason">An optional reason surfaced on the checkpoint.</param>
    /// <exception cref="InvalidOperationException">
    /// A checkpoint has already been requested for this batch.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation has been requested on the context's token.
    /// </exception>
    public void Checkpoint(string? reason = null) {
        if (_checkpointRequested)
            throw new InvalidOperationException("Checkpoint already requested for this batch");

        CancellationToken.ThrowIfCancellationRequested();

        _checkpointRequested = true;
        _checkpointReason    = reason;
    }

    /// <summary>
    /// Requests that processing stop after this batch. With <c>AutoTrack = true</c> the framework
    /// auto-tracks the batch first; either way it then injects a shutdown marker and dispatches no
    /// further batches. Pending positions are committed on the way out.
    /// </summary>
    /// <param name="reason">An optional reason surfaced on shutdown.</param>
    public void StopProcessing(string? reason = null) {
        _stopRequested = true;
        _stopReason    = reason;
    }

    bool    _checkpointRequested;
    string? _checkpointReason;

    bool    _stopRequested;
    string? _stopReason;

    bool    _trackedByUser;

    internal bool    CheckpointRequested => _checkpointRequested;
    internal string? CheckpointReason    => _checkpointReason;

    internal bool    StopRequested       => _stopRequested;
    internal string? StopReason          => _stopReason;
    internal bool    TrackedByUser       => _trackedByUser;
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
}
