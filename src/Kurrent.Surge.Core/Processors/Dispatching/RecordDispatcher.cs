using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Dispatches a batch one record at a time to an <see cref="IRecordProcessor"/>, with per-record
/// telemetry, per-record auto-track, and a partial-batch stop.
/// </summary>
sealed class RecordDispatcher(
    IRecordProcessor processor,
    Checkpointer checkpointer,
    ProgressTracker progress,
    ObserverDispatcher observers,
    ProcessorOptions options,
    TimeProvider timeProvider
) : ProcessorDispatcher(processor, checkpointer, progress, observers, options, timeProvider) {
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
        // Ask before dispatching: a processor that handles only some message types reports the rest
        // instead of consuming them silently. No handler runs and no processing span opens.
        if (!processor.CanProcess(record)) {
            Observers.RecordSkipped(new(Info, record, TimeProvider.GetUtcNow()));

            // Throughput counts what was consumed, so a skipped record counts here; the
            // records_processed metric rides on RecordCompleted and therefore does not.
            Progress.CountThroughput(record);

            // Advance the frontier past it, or a stream of unhandled types never commits and the
            // processor replays it forever. Nothing claimed the record, so this is the framework's call.
            if (AutoTrack)
                Track(record.Position);

            return RecordProcessingResult.Continue;
        }

        // Ready carries the live record: observers enrich metadata and open the tracing span here, and
        // the handler sees the mutation.
        Observers.RecordReady(new(Info, record, TimeProvider.GetUtcNow()));

        // Track is transparent — it advances the checkpointer AND progress together — so a user ctx.Track
        // updates the gauges, not just the commit position (see ProcessorDispatcher.Track).
        var ctx = context.ForProcessRecord(record, requestTrack: Track);
        
        var start   = TimeProvider.GetTimestamp();
        var outcome = RecordOutcome.Processed;

        Exception? error = null;

        try {
            await processor.ProcessRecord(ctx).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            outcome = RecordOutcome.Cancelled;
            throw;
        }
        catch (Exception ex) {
            outcome = RecordOutcome.Failed;
            error   = ex;
            throw DroneException.ProcessRecordFailed(ex);
        }
        finally {
            // Measured at handler exit, before the bookkeeping below — the manual checkpoint raise awaits
            // a channel write, which is framework time and does not belong in the handler's duration.
            var elapsed = TimeProvider.GetElapsedTime(start);

            // The bookkeeping only runs on the success path; a throw unwinds straight through here, so the
            // terminal event still fires exactly once and reports the progress as it stands.
            if (outcome is RecordOutcome.Processed) {
                // Count every dispatched record toward throughput, independent of tracking mode.
                Progress.CountThroughput(record);

                // Auto-track this record only when AutoTrack is on and no stop was requested; otherwise the
                // user owns the frontier via ctx.Track (already transparent above). Re-tracking the same
                // position is idempotent on progress and harmless on the checkpointer snapshot.
                if (AutoTrack && !ctx.StopRequested)
                    Track(record.Position);
            }

            Observers.RecordCompleted(new(Info, record, outcome, elapsed, Progress.Progress, TimeProvider.GetUtcNow(), error));
        }

        // Stop supersedes a pending manual checkpoint: once a stop is requested the graceful Shutdown
        // barrier is the single OnCheckpoint + commit point, so raising a Manual barrier here would
        // double-commit. ctx.Checkpoint() already threw if a stop was requested before it.
        if (ctx.CheckpointRequested && !ctx.StopRequested)
            await Checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
    }
}
