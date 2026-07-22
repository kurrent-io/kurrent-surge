using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Diagnostics;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Dispatches a batch one record at a time to an <see cref="IRecordProcessor"/>, with per-record
/// telemetry, per-record auto-track, and a partial-batch stop.
/// </summary>
sealed class RecordDispatcher(
    IRecordProcessor processor,
    Checkpointer checkpointer,
    ProgressTracker progress,
    DroneTelemetry telemetry,
    ProcessorOptions options
) : ProcessorDispatcher(processor, checkpointer, progress, telemetry, options) {
    public override async ValueTask<RecordProcessingResult> Dispatch(DroneContext context, RecordBatch batch) {
        foreach (var record in batch) {
            var result = await DispatchRecord(context, record).ConfigureAwait(false);

            // A stop skips the remaining records in this batch — they were delivered to the dispatcher
            // but never to the handler, so nothing claims them and a resuming run redelivers them.
            if (result.ShouldStop)
                return result;
        }

        return RecordProcessingResult.Continue;
    }

    async ValueTask<RecordProcessingResult> DispatchRecord(DroneContext context, Record record) {
        using var scope = Telemetry.TrackRecord();

        // Track is transparent — it advances the checkpointer AND progress together — so a user ctx.Track
        // updates the gauges, not just the commit position (see ProcessorDispatcher.Track).
        var ctx = context.ForProcessRecord(Progress.Progress, record, requestTrack: Track);

        try {
            await processor.ProcessRecord(ctx).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.ProcessRecordFailed(ex);
        }

        // Count every dispatched record toward throughput, independent of tracking mode.
        Progress.CountThroughput(record);

        // Auto-track this record only when AutoTrack is on and no stop was requested; otherwise the user
        // owns the frontier via ctx.Track (already transparent above). Re-tracking the same position is
        // idempotent on progress and harmless on the checkpointer snapshot.
        if (AutoTrack && !ctx.StopRequested)
            Track(record.Position);

        // Stop supersedes a pending manual checkpoint: once a stop is requested the graceful Shutdown
        // barrier is the single OnCheckpoint + commit point, so raising a Manual barrier here would
        // double-commit. ctx.Checkpoint() already threw if a stop was requested before it.
        if (ctx.CheckpointRequested && !ctx.StopRequested)
            await Checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        // Cheap scalar reads — never materialize the full DroneProgress snapshot on the per-record path.
        scope.Complete(Progress.TotalRecords, Progress.UncommittedRecords, Progress.UncommittedBytes);

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
    }
}
