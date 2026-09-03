using Kurrent.Surge.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Configuration;

namespace Kurrent.Surge.Processors;

/// <summary>
/// Defines the interface for processing records in batches. The drone accumulates records up to
/// <c>Batching.MaxBatchSize</c> or until <c>Batching.BatchWindow</c> elapses (whichever comes first),
/// then dispatches the batch via <see cref="ProcessBatch"/>. Lifecycle hooks (Connected, Checkpoint)
/// and shutdown signals always flush the pending batch before yielding.
/// </summary>
/// <remarks>
/// <para>
/// Batch size and window are configured on <see cref="ProcessorOptions"/>
/// (<c>opts.Batching</c>), not on the processor.
/// </para>
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

    public required RecordBatch Records  { get; init; }

    internal Action<RecordPosition> RequestTrack { get; init; } = static _ => { };

    /// <summary>
    /// Marks the whole batch as durably handled — tracks every record's position. Composes
    /// idempotently with auto-track; the tracker keeps the latest position per partition.
    /// </summary>
    public void Track() {
        _trackedByUser = true;
        TrackFrontier(RecordPosition.Latest); // Latest never caps — claim every record
    }

    /// <summary>
    /// Marks the batch handled up to and including <paramref name="upTo"/> — a prefix. Records past
    /// it stay pending. The tracker is strictly monotonic, so positions at or below the last
    /// committed position are skipped.
    /// </summary>
    /// <param name="upTo">The last position to claim; records beyond it remain untracked.</param>
    public void Track(RecordPosition upTo) {
        _trackedByUser = true;
        TrackFrontier(upTo);
    }

    /// <summary>
    /// Marks one record as durably handled, directly and cheaply — no frontier scan. Call it after
    /// applying each record, and the tracked frontier equals the applied work at every instant: on a
    /// mid-batch throw, the final checkpoint commits exactly the records whose effects exist.
    /// </summary>
    /// <remarks>
    /// Prefer this inside a per-record loop; <see cref="Track(RecordPosition)"/> folds the whole
    /// batch per call and is meant for one sparse claim, not for every record.
    /// </remarks>
    public void Track(Record record) {
        _trackedByUser = true;
        RequestTrack(record.Position);
    }

    // The commit frontier is the max position per partition: the strictly-monotonic tracker only ever
    // reads the last position per partition, so tracking each record individually is dead work. Resolve
    // the per-partition max in one pass (capped at upTo), then request a single track per partition.
    // Filtering inline avoids a LINQ iterator allocation on the track hot path.
    void TrackFrontier(RecordPosition upTo) {
        Dictionary<Partition, RecordPosition>? frontier = null;

        foreach (var record in Records) {
            var position = record.Position;

            if (position > upTo)
                continue;

            frontier ??= new();

            if (!frontier.TryGetValue(position.Partition, out var current) || position > current)
                frontier[position.Partition] = position;
        }

        if (frontier is null)
            return;

        foreach (var position in frontier.Values)
            RequestTrack(position);
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
    /// A checkpoint has already been requested for this batch, or a stop has already been requested
    /// (after <see cref="StopProcessing"/> the shutdown barrier is the single commit point).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation has been requested on the context's token.
    /// </exception>
    public void Checkpoint(string? reason = null) {
        if (_checkpointRequested)
            throw new InvalidOperationException("Checkpoint already requested for this batch");

        // Once a stop is requested the graceful Shutdown barrier becomes the single OnCheckpoint +
        // commit point, so a manual checkpoint is meaningless — reject it rather than queue a barrier
        // the drone will suppress.
        if (_stopRequested)
            throw new InvalidOperationException("Cannot request a checkpoint after StopProcessing.");

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
/// Convenience base class for batch processors. Exposes the lifecycle hooks as virtual methods so
/// derived classes can <c>override</c> them. Batch size and window are configured on
/// <see cref="ProcessorOptions"/> (<c>opts.Batching</c>), not on the processor.
/// </summary>
[PublicAPI]
public abstract class RecordBatchProcessor : IRecordBatchProcessor {
    public abstract ValueTask ProcessBatch(BatchContext context);

    public virtual ValueTask OnInitialize(InitializeContext context) => ValueTask.CompletedTask;
    public virtual ValueTask OnConnected(ConnectedContext context)   => ValueTask.CompletedTask;
    public virtual ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;
}
