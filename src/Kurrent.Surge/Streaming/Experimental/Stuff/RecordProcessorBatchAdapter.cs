using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Streaming.Diagnostics;

namespace Kurrent.Streaming;

/// <summary>
/// EXPERIMENTAL SKETCH — not integrated, not wired into the Drone, intentionally not finished.
/// <para>
/// Adapts an <see cref="IRecordProcessor"/> (single-record dispatch — e.g. <see cref="MessageProcessor"/>)
/// onto the <see cref="IRecordBatchProcessor"/> contract, so the Drone can run ONE dispatch path
/// (batches only) while record-at-a-time processors keep working unchanged. Every per-record
/// implementation that wants single-message semantics would go through an adapter like this one.
/// </para>
/// <para>
/// The point of the exercise: the per-record logic in <c>Drone.HandleRecord</c> /
/// <c>DispatchPerRecord</c> is NOT a type conversion — it is framework bookkeeping (catch-up filter,
/// telemetry scope, auto-track-after-handler, manual-track mirroring, manual checkpoint barrier,
/// mid-batch stop with partial commit). Moving it here means the adapter must hold the same
/// Drone-internal collaborators <c>HandleRecord</c> uses (<see cref="Checkpointer"/>,
/// <see cref="ProgressTracker"/>, <see cref="DroneTelemetry"/>, the catch-up map). NONE of them are
/// reachable through the public <see cref="BatchContext"/> surface — which is why this is an
/// internal type the Drone would construct, not a user-facing batch processor.
/// </para>
/// </summary>
/// <remarks>
/// OPEN / UNRESOLVED for integration — captured here so the sketch is honest, not silent:
/// <list type="number">
/// <item>
/// <b>Bookkeeping ownership.</b> <c>Drone.HandleBatch</c> (Drone.cs:293-339) runs its OWN count/track
/// loop over the whole batch AFTER <c>ProcessBatch</c> returns. If this adapter also does per-record
/// bookkeeping, that loop double-counts. Integration requires the Drone to stop running its batch
/// bookkeeping for an adapter-wrapped processor — i.e. bookkeeping moves OUT of the Drone into the
/// adapter family — OR the adapter degrades to batch granularity (losing the per-record fidelity this
/// whole exercise exists to preserve).
/// </item>
/// <item>
/// <b>Stop propagation.</b> <c>HandleRecord</c> stops by raising a Shutdown barrier on the
/// <see cref="Checkpointer"/>, which completes the channel and drains the Drone loop. This adapter
/// does the same directly. The Drone-level <c>stopping</c> flag (Drone.cs:169, which also skips
/// trailing server watermarks) is not reachable from here.
/// </item>
/// <item>
/// <b>AutoTrack STAYS</b> (decided 2026-06-18). The <c>AutoTrack=true/false</c> branch is preserved
/// verbatim — the <c>false</c> path (manual <c>ctx.Track</c>, e.g. produce-async-then-track-on-ack)
/// must keep working, so the per-record context is wired to the real <see cref="Checkpointer.Track"/>.
/// </item>
/// </list>
/// </remarks>
sealed class RecordProcessorBatchAdapter(
    IRecordProcessor inner,
    Checkpointer checkpointer,
    ProgressTracker progress,
    DroneTelemetry telemetry,
    IReadOnlyDictionary<Partition, LogPosition> catchUp,
    bool autoTrack
) : IRecordBatchProcessor {
    /// <summary>
    /// Port of <c>Drone.DispatchPerRecord</c> (Drone.cs:233-251): iterate the batch, filter caught-up
    /// records before dispatch, hand each survivor to the per-record path, stop the batch early on
    /// request.
    /// </summary>
    public async ValueTask ProcessBatch(BatchContext context) {
        foreach (var record in context.Records) {
            // Catch-up filter runs BEFORE dispatch — a record already committed in a prior run never
            // reaches the handler. This is the per-record guarantee the batch path does NOT give:
            // HandleBatch delivers the whole batch and filters only its track loop (Drone.cs:230-232).
            // Reproducing it here is precisely why the adapter, not the user's processor, owns the loop.
            if (catchUp.TryGetValue(record.Position.Partition, out var cp)
             && record.Position.LogPosition <= cp)
                continue;

            var stop = await DispatchRecord(context, record).ConfigureAwait(false);

            if (stop)
                break; // remaining records in this batch are skipped — partial-batch stop
        }
    }

    /// <summary>
    /// Port of <c>Drone.HandleRecord</c> (Drone.cs:254-291). Returns <c>true</c> when the handler
    /// requested stop.
    /// </summary>
    async ValueTask<bool> DispatchRecord(BatchContext context, Record record) {
        using var scope = telemetry.TrackRecord();

        // BatchContext IS a DroneContext, so it carries the identity fields; ForProcessRecord wires the
        // per-record context's tracker to the SAME Checkpointer.Track the Drone uses, so the user's
        // ctx.Track() reaches the real tracker immediately. The AutoTrack=false
        // produce-async-then-track-on-ack pattern depends on this being the live tracker, not a copy.
        var ctx = context.ForProcessRecord(progress.Progress, record, requestTrack: checkpointer.Track);

        await inner.ProcessRecord(ctx).ConfigureAwait(false);

        // Count every dispatched record toward throughput, independent of tracking mode.
        progress.CountThroughput(record);

        if (autoTrack && !ctx.StopRequested) {
            checkpointer.Track(record.Position);
            progress.TrackPosition(record.Position);
        }
        else if (ctx.TrackedByUser) {
            // Manual track (auto-track off, or a stop record the user explicitly claimed): the
            // checkpointer was already advanced by ctx.Track; mirror the position into progress.
            progress.TrackPosition(record.Position);
        }

        if (ctx.CheckpointRequested)
            await checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        // Cheap scalar reads — never materialize the full DroneProgress snapshot on the per-record path.
        scope.Complete(progress.TotalRecords, progress.UncommittedRecords, progress.UncommittedBytes);

        if (!ctx.StopRequested)
            return false;

        // The stop record is NOT auto-tracked (the !ctx.StopRequested guard above) — the handler must
        // call ctx.Track first to claim it. Shutdown commits exactly the tracked positions and
        // completes the channel so the Drone loop drains.
        await checkpointer
            .RaiseCheckpoint(CheckpointTrigger.Shutdown, ctx.StopReason)
            .ConfigureAwait(false);

        return true;
    }

    // Lifecycle hooks pass straight through to the wrapped processor.
    public ValueTask OnConnected(ConnectedContext context)   => inner.OnConnected(context);
    public ValueTask OnCheckpoint(CheckpointContext context) => inner.OnCheckpoint(context);
}
