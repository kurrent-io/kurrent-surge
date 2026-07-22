using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Owns the dispatch of a record batch to one processor shape, including that shape's bookkeeping
/// (telemetry scope, auto-track granularity, manual checkpoint requests). The drone stays
/// shape-agnostic: one <see cref="Dispatch"/> call per batch, stop reported through the result.
/// </summary>
/// <remarks>
/// Deliberately not one of the public processor interfaces: <see cref="IRecordBatchProcessor.ProcessBatch"/>
/// returns a bare <see cref="ValueTask"/>, so a stop could not be reported back to the run loop — and
/// without that signal the loop cannot suppress trailing server watermarks after a stop, which
/// advances the checkpoint past the last dispatched record.
/// </remarks>
abstract class ProcessorDispatcher(
    IProcessor processor,
    Checkpointer checkpointer,
    ProgressTracker progress,
    ObserverDispatcher observers,
    ProcessorOptions options
) {
    protected Checkpointer       Checkpointer { get; } = checkpointer;
    protected ProgressTracker    Progress     { get; } = progress;
    protected ObserverDispatcher Observers    { get; } = observers;
    protected bool               AutoTrack    { get; } = options.Checkpoint.AutoTrack;

    protected ProcessorInfo Info { get; } = new(options.ProcessorId, options.DroneId);

    /// <summary>
    /// Dispatches a batch to the processor shape this dispatcher owns.
    /// </summary>
    /// <remarks>
    /// <see cref="DroneContext"/> is mutable drone state — the session lands on Connected, the
    /// cancellation token on RunAsync — so it arrives per call and is never captured in a field.
    /// </remarks>
    public abstract ValueTask<RecordProcessingResult> Dispatch(DroneContext context, RecordBatch batch);

    public async ValueTask OnConnected(ConnectedContext context) {
        try {
            await processor.OnConnected(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.OnConnectedFailed(ex);
        }
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
        try {
            await processor.OnCheckpoint(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.OnCheckpointFailed(ex);
        }
    }

    // The transparent tracking primitive — advances the checkpointer AND progress together. Wired as the
    // batch/record RequestTrack delegate so a user ctx.Track* updates the uncommitted gauges (closing the
    // transparency gap), and used by auto-track so every tracking source folds progress identically.
    protected void Track(RecordPosition position) {
        Checkpointer.Track(position);
        Progress.TrackPosition(position);
    }
}
