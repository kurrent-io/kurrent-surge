using Kurrent.Surge.Checkpointing;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// Dispatches a whole batch to an <see cref="IRecordBatchProcessor"/>, with one telemetry scope
/// around the handler and a whole-batch frontier auto-track.
/// </summary>
class BatchDispatcher(
    IRecordBatchProcessor processor,
    Checkpointer checkpointer,
    ProgressTracker progress,
    ObserverDispatcher observers,
    ProcessorOptions options,
    TimeProvider timeProvider
) : ProcessorDispatcher(processor, checkpointer, progress, observers, options, timeProvider) {
    readonly int _maxParallelism = options.MaxParallelism;

    public override async ValueTask<RecordProcessingResult> Dispatch(DroneContext context, RecordBatch batch) {
        // Ready carries the live records: observers enrich metadata and open the tracing span here, and
        // the handler sees the mutation.
        Observers.BatchReady(new(Info, batch, TimeProvider.GetUtcNow()));

        // Track is transparent (checkpointer + progress together), so a user ctx.Track updates the gauges.
        var ctx = context.ForBatch(batch, requestTrack: Track);

        var start     = TimeProvider.GetTimestamp();
        var outcome   = RecordOutcome.Processed;
        var processed = 0;

        Exception? error = null;

        try {
            await processor.ProcessBatch(ctx).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            outcome = RecordOutcome.Cancelled;
            throw;
        }
        catch (Exception ex) {
            outcome = RecordOutcome.Failed;
            error   = ex;
            throw DroneException.ProcessBatchFailed(ex);
        }
        finally {
            // Measured at handler exit: the frontier fold, throughput count and frontier scan below are
            // framework bookkeeping and do not belong in the handler's duration.
            var elapsed = TimeProvider.GetElapsedTime(start);

            // The bookkeeping only runs on the success path; a throw unwinds straight through here, so the
            // terminal event still fires exactly once with ProcessedCount 0 — the frontier is then whatever
            // the user tracked before throwing, and the Shutdown barrier commits exactly that.
            if (outcome is RecordOutcome.Processed) {
                // Resolve the committed frontier (the invariant): auto-track the whole-batch frontier (max
                // position per partition) only when AutoTrack is on AND no stop was requested — a stop caps
                // the commit at exactly what the user tracked. Otherwise the commit is the user's tracks.
                if (AutoTrack && !ctx.StopRequested)
                    TrackFrontier(batch);

                // Throughput counts every dispatched record (the "batch trap": delivered records count even
                // with AutoTrack off and nothing committed). The batch is pre-pruned upstream, so Count is
                // the dispatched count — advanced in one step, not a per-record loop.
                if (batch.Count > 0)
                    Progress.CountThroughput(batch.Count, batch[^1].Timestamp);

                // records_processed is the committed/durable prefix: records at or below the resolved
                // frontier (PendingPositions = the last tracked position per partition). It never
                // over-counts a stop's un-committed remainder, and counts a replay only when it is
                // eventually committed, never twice.
                processed = CountUpToFrontier(batch, Checkpointer.PendingPositions());
            }

            Observers.BatchCompleted(
                new(Info, batch, outcome, processed, elapsed, Progress.Progress, TimeProvider.GetUtcNow(), error));
        }

        // Stop supersedes a pending manual checkpoint: once a stop is requested the graceful Shutdown
        // barrier is the single OnCheckpoint + commit point, so raising a Manual barrier here would
        // double-commit. ctx.Checkpoint() already threw if a stop was requested before it.
        if (ctx is { CheckpointRequested: true, StopRequested: false })
            await Checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
    }

    // Auto-track the whole batch: one pass to the max position per partition, then Track each through the
    // transparent primitive. NOT PartitionedPositionTracker.Track(IEnumerable) — that folds max-per-partition
    // but bypasses progress, and tracking must fold progress too.
    void TrackFrontier(RecordBatch batch) {
        Dictionary<Partition, RecordPosition>? frontier = null;

        foreach (var record in batch) {
            var position = record.Position;

            // At most one entry per owned partition, never per record — size to the partition count.
            frontier ??= new(_maxParallelism);

            if (!frontier.TryGetValue(position.Partition, out var current) || position > current)
                frontier[position.Partition] = position;
        }

        if (frontier is null)
            return;

        foreach (var position in frontier.Values)
            Track(position);
    }

    // Effective batch size = the count of records at or below the committed frontier. The frontier is the
    // last tracked position per partition (PendingPositions); a record counts when its position is at or
    // below its partition's frontier. Index the frontier by partition once (it holds one entry per owned
    // partition) so the per-record check is a single O(1) lookup rather than a scan.
    static int CountUpToFrontier(RecordBatch batch, RecordPosition[] frontier) {
        if (frontier.Length == 0 || batch.Count == 0)
            return 0;

        var ceilingByPartition = new Dictionary<Partition, LogPosition>(frontier.Length);
        foreach (var f in frontier)
            ceilingByPartition[f.Partition] = f.LogPosition;

        var count = 0;

        foreach (var record in batch) {
            if (ceilingByPartition.TryGetValue(record.Position.Partition, out var ceiling)
             && record.Position.LogPosition <= ceiling)
                count++;
        }

        return count;
    }
}
