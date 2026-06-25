using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Diagnostics;
using Kurrent.Streaming.Gateway;

namespace Kurrent.Streaming;

/// <summary>
/// EXPERIMENTAL SKETCH — not integrated, not wired into DI, intentionally not finished. A copy of
/// <see cref="Drone"/> with the per-record dispatch path excised, to show what the runner looks like
/// when <see cref="IRecordBatchProcessor"/> is the SOLE contract it knows.
/// <para>
/// What changed vs <see cref="Drone"/>:
/// </para>
/// <list type="bullet">
/// <item>The constructor takes an <see cref="IRecordBatchProcessor"/> directly (not <see cref="IProcessor"/>),
/// so there is no <c>processor switch</c>. A per-record processor (<see cref="IRecordProcessor"/>,
/// <see cref="MessageProcessor"/>) reaches this Drone already wrapped in
/// <see cref="RecordProcessorBatchAdapter"/> by the registration layer.</item>
/// <item>The <c>Run</c> loop's <c>case RecordBatch</c> dispatches straight to <see cref="HandleBatch"/> —
/// the inner <c>switch (_processor)</c>, the <c>DispatchPerRecord</c> local, and <see cref="Drone"/>'s
/// <c>HandleRecord</c> are all gone.</item>
/// </list>
/// </summary>
/// <remarks>
/// OPEN / UNRESOLVED — the bookkeeping-ownership fork (carried, not decided):
/// <para>
/// <see cref="HandleBatch"/> still runs the framework count/track loop over the whole batch, exactly as
/// <see cref="Drone"/> does today. That is correct for a NATIVE batch processor. But when the wrapped
/// processor is a <see cref="RecordProcessorBatchAdapter"/> — which does its OWN per-record count/track/
/// checkpoint inside <c>ProcessBatch</c> — this loop bookkeeps a SECOND time → double counting and
/// double tracking. This sketch deliberately does NOT resolve that; it shows the batch-only shape. The
/// fork is: either bookkeeping leaves the Drone into the adapter family (per-record adapter + a batch
/// runner, Drone shrinks to subscribe→batch→dispatch→barriers), or the Drone keeps one loop and the
/// adapter degrades to batch-granular semantics. AutoTrack STAYS (decided 2026-06-18) either way.
/// </para>
/// </remarks>
sealed class BatchDrone : IDrone {
    readonly ISubscription        _subscription;
    readonly IRecordBatchProcessor _processor;
    readonly Checkpointer         _checkpointer;
    readonly StreamsClient        _streams;
    readonly DroneTelemetry       _telemetry;
    readonly ProgressTracker      _progress;
    readonly HybridLogicalClock   _clock;
    readonly TimeProvider         _timeProvider;

    readonly Channel<SubscribeMessage> _channel;

    SourceLagProbe? _sourceLagProbe;

    int _started;

    DroneContext DroneContext { get; set; }

    public BatchDrone(
        ProcessorOptions options,
        ISubscription subscription,
        Checkpointer checkpointer,
        Channel<SubscribeMessage> channel,
        StreamsClient streams,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IRecordBatchProcessor processor
    ) {
        Options = options;

        _subscription = subscription;
        _checkpointer = checkpointer;
        _channel      = channel;
        _streams      = streams;
        _clock        = new HybridLogicalClock(timeProvider);
        _timeProvider = timeProvider;

        // One contract. Per-record processors are pre-wrapped in RecordProcessorBatchAdapter before they
        // get here, so there is nothing to branch on — the runner only ever sees a batch processor.
        _processor = new SafeRecordBatchProcessor(processor);

        _progress = new ProgressTracker(timeProvider, options.MaxParallelism);

        var logger = loggerFactory.CreateLogger<BatchDrone>();

        _telemetry = new DroneTelemetry(logger, options.ProcessorId, options.DroneId);

        DroneContext = new DroneContext {
            ProcessorId = ProcessorId,
            DroneId     = DroneId,
            SessionId   = SessionId.Unset,
            Logger      = logger
        };
    }

    public string ProcessorId => Options.ProcessorId;
    public string DroneId     => Options.DroneId;

    public ProcessorOptions Options { get; }

    public DroneStatus Status { get; private set; } = DroneStatus.Starting;

    public DroneProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        Status = DroneStatus.Starting;

        _telemetry.Starting();

        DroneContext = DroneContext with { CancellationToken = stoppingToken };

        var partitions = Enumerable
            .Range(0, Options.MaxParallelism)
            .Select(Partition.From)
            .ToArray();

        // Resolve explicit start position if provided (stream info may be missing)
        var resetTo = RecordPosition.Unset;

        if (Options.Subscription.StartPosition is { } explicitStart) {
            if (explicitStart.Stream.IsUnset) {
                var record = await _streams
                    .InspectRecord(explicitStart.LogPosition, stoppingToken)
                    .ConfigureAwait(false);

                explicitStart = RecordPosition.From(record.Position.LogPosition, record.Stream, record.Revision);
            }

            resetTo = explicitStart;
        }

        // Load checkpoint (resetting first if explicit start was provided), start timer
        var startPositions = await _checkpointer
            .Initialize(partitions, Options.Subscription.InitialPosition, resetTo, stoppingToken)
            .ConfigureAwait(false);

        // Catch-up filter: skip records at or below per-partition checkpoint
        var catchUp = startPositions.ToDictionary(p => p.Partition, p => p.LogPosition);

        var lowWatermark = startPositions.Length > 0
            ? startPositions.Min()
            : RecordPosition.Earliest;

        // Subscribe writes to channel, completes it when done. Never throws.
        _ = _subscription.Subscribe(
            lowWatermark, Options.Subscription.Filter,
            Options.SkipDecoding, stoppingToken);

        // Optional source-lag probe (opt-in). It reads the readable log head on a fixed cadence —
        // the only way a gRPC client learns the head moved ahead — and reports the lag gauges.
        // Always an unfiltered head read; a filtered backward read would scan the whole log.
        if (Options.TrackSourceLag) {
            _sourceLagProbe = new(
                readHead: ct => _streams.InspectRecord(LogPosition.Latest, skipDecoding: true, ct),
                snapshot: () => Progress,
                onSample: _telemetry.SourceLagSample,
                timeProvider: _timeProvider);

            _sourceLagProbe.Start(stoppingToken);
        }

        Exception? fatalError = null;

        try {
            await Run(catchUp).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex) {
            fatalError = ex.InnerException;
        }
        catch (Exception ex) {
            fatalError = ex;
        }

        // Channel closed — subscription ended or user stopped.
        // If user stopped, the Shutdown barrier already committed pending positions.
        // If subscription ended naturally, pending positions may remain.
        // Skip if we already have a fatal error — the checkpoint hook would fail again.
        if (fatalError is null && _checkpointer.HasPendingPositions) {
            var now = _clock.Now();
            var checkpoint = new ShutdownCheckpoint(new CheckpointId(now), now.DateTimeOffset);
            await HandleCheckpoint(checkpoint, partitions).ConfigureAwait(false);
        }

        await Shutdown(fatalError).ConfigureAwait(false);
    }

    async Task Run(Dictionary<Partition, LogPosition> catchUp) {
        // The drone's owned partitions — the broadcast target for server watermarks (see HandleCheckpoint).
        var partitions = catchUp.Keys.ToArray();
        var stopping   = false;

        // Single batched read of the channel: records accumulate into a RecordBatch; Connected and
        // Checkpoint markers flush the pending batch and pass through. Batch size/window come from
        // options, not the processor — every flushed batch dispatches through the one ProcessBatch path.
        var stream = _channel.Reader
            .ReadAllAsync(CancellationToken.None)
            .ReadBatched<SubscribeMessage, Record, BatchedSubscribeMessage>(
                Options.Batching.MaxBatchSize, Options.Batching.BatchWindow,
                classify: static msg => msg.Match<BatchAction<Record, BatchedSubscribeMessage>>(
                    static connected  => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(connected),
                    static record     => BatchAction<Record, BatchedSubscribeMessage>.Batch(record),
                    static checkpoint => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(checkpoint)),
                batchToOutput: static records => RecordBatch.From(records),
                CancellationToken.None);

        await foreach (var output in stream.ConfigureAwait(false)) {
            // Outer stop scope: once stopping, skip later batches AND server scan watermarks
            // (Filter / CaughtUp). The batched read pulls a trailing watermark in ahead of the stop;
            // folding it would advance the checkpoint past the last dispatched record (matching records
            // beyond the stop were skipped, not processed), so a resuming run would start past its own
            // stop record and hang on a live subscription. Only the injected Shutdown barrier — which
            // commits exactly the tracked positions — is honored after stop.
            if (stopping && output.Value is RecordBatch or FilterCheckpoint or CaughtUpCheckpoint)
                continue;

            switch (output.Value) {
                case Connected c:
                    await HandleConnected(c).ConfigureAwait(false);
                    break;

                // One dispatch path: a flushed batch always goes to ProcessBatch. A per-record stop
                // bubbles up as RecordProcessingResult.ShouldStop the same way it did on the old
                // per-record arm — except now it travels through the adapter's ctx.StopProcessing on
                // the BatchContext rather than a Drone-owned per-record loop.
                case RecordBatch batch: {
                    var result = await HandleBatch(_processor, batch, catchUp).ConfigureAwait(false);

                    if (result.ShouldStop && !stopping) {
                        stopping = true;

                        await _checkpointer
                            .RaiseCheckpoint(CheckpointTrigger.Shutdown, result.Reason)
                            .ConfigureAwait(false);
                    }

                    break;
                }

                case Checkpoint checkpoint:
                    await HandleCheckpoint(checkpoint, partitions).ConfigureAwait(false);
                    break;
            }
        }
    }

    async ValueTask<RecordProcessingResult> HandleBatch(
        IRecordBatchProcessor processor,
        RecordBatch batch,
        Dictionary<Partition, LogPosition> catchUp
    ) {
        var autoTrack = Options.Checkpoint.AutoTrack;

        var ctx = DroneContext.ForBatch(
            Progress, batch,
            requestTrack: _checkpointer.Track);

        await processor
            .ProcessBatch(ctx)
            .ConfigureAwait(false);

        // Count and track are decoupled: every dispatched record counts toward throughput (even
        // with AutoTrack off — the "batch trap"), but positions are claimed only when auto-track
        // is on. With it off the user owns progress via ctx.Track() / ctx.Track(upTo), which
        // advanced the checkpointer above. Auto-track is batch-granular, so commit cadence aligns
        // with batch boundaries.
        //
        // ⚠️ FORK (see class remarks): when `processor` is a RecordProcessorBatchAdapter this loop is a
        // SECOND bookkeeper — the adapter already counted/tracked each record. Resolving that is the
        // ownership decision this sketch defers.
        foreach (var record in batch) {
            // Catch-up filter: skip records already processed in a prior run
            if (catchUp.TryGetValue(record.Position.Partition, out var cp)
             && record.Position.LogPosition <= cp)
                continue;

            using var scope = _telemetry.TrackRecord();
            _progress.CountThroughput(record);

            if (autoTrack) {
                _checkpointer.Track(record.Position);
                _progress.TrackPosition(record.Position);
            }

            // Cheap scalar reads — never materialize the full DroneProgress snapshot per record.
            scope.Complete(_progress.TotalRecords, _progress.UncommittedRecords, _progress.UncommittedBytes);
        }

        if (ctx.CheckpointRequested)
            await _checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
    }

    #region ->> Message Handlers <<-

    async ValueTask<RecordProcessingResult> HandleConnected(Connected connected) {
        DroneContext = DroneContext with { SessionId = connected.SessionId };
        Status       = DroneStatus.Processing;

        _telemetry.Processing(connected.SessionId);

        await _processor
            .OnConnected(DroneContext.ForConnected())
            .ConfigureAwait(false);

        return RecordProcessingResult.Continue;
    }

    async ValueTask HandleCheckpoint(Checkpoint checkpoint, Partition[] partitions) {
        var trigger = checkpoint.TriggerKind;
        var start   = _timeProvider.GetTimestamp();

        _telemetry.CheckpointInitiated(checkpoint.Id, trigger);

        // Server-emitted log watermarks — FilterCheckpoint and CaughtUpCheckpoint — carry the log
        // position the shared scan has reached over regions with no matching records. The barrier
        // knows only a log position and has no concept of partitions; folding it across owned
        // partitions is this code's job. The client resolves a stream CaughtUp's log position up
        // front (see StreamsClient.Subscribe), so these always carry one; the client-timer
        // IntervalCheckpoint and processor barriers are not server watermarks.
        var scanPosition = checkpoint is FilterCheckpoint or CaughtUpCheckpoint
                        && checkpoint.Positions is [.., var watermark]
            ? watermark
            : (RecordPosition?)null;

        // Watermark folding is auto-tracking, so it is gated by AutoTrack. When on, fold the
        // watermark across every owned partition before snapshotting so a cold restart skips the
        // already-scanned span — safe because the channel is read in order: every record at or below
        // this position is already processed by the time we see the checkpoint. When off, advance
        // nothing — the position is surfaced to OnCheckpoint (ScanPosition) and the user decides
        // whether to ctx.Track past it. Nothing is silently suppressed.
        if (Options.Checkpoint.AutoTrack && scanPosition is { } autoWatermark)
            _checkpointer.AdvanceWatermark(autoWatermark.LogPosition, partitions);

        var checkpointToken = CancellationToken.None;

        // Pending positions as the barrier opens — informational for the handler.
        var pending = _checkpointer.PendingPositions();

        // OnCheckpoint fires on EVERY barrier and runs BEFORE the commit snapshot, so positions the
        // handler tracks (e.g. ctx.Track(ctx.ScanPosition!)) land in this barrier's commit. The track
        // delegate folds the given log position across owned partitions — the partition accounting
        // the barrier itself cannot do.
        var ctx = DroneContext.ForCheckpoint(
            checkpoint, pending, scanPosition,
            requestTrack: pos => _checkpointer.AdvanceWatermark(pos.LogPosition, partitions)
        ) with { CancellationToken = checkpointToken };

        try {
            await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.Id, trigger, pending, ex);

            if (Options.Debug)
                throw;

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;

            return;
        }

        // Snapshot AFTER the callback so the handler's tracks are included. Commit only when
        // something is pending — an empty barrier (e.g. an interval tick or a watermark the user
        // chose not to track) still fired OnCheckpoint above but commits nothing.
        var positions = _checkpointer.PendingPositions();

        if (positions.Length == 0)
            return;

        try {
            await _checkpointer
                .CommitCheckpoint(checkpoint.Id, positions, checkpointToken)
                .ConfigureAwait(false);

            _progress.TrackCheckpoint(checkpoint, positions);
            _telemetry.CheckpointComplete(checkpoint.Id, trigger, positions, _timeProvider.GetElapsedTime(start).TotalSeconds);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.Id, trigger, positions, ex);

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;
        }
    }

    #endregion

    #region ->> Shutdown <<-

    async ValueTask Shutdown(Exception? fatalError = null) {
        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        List<Exception> errors = [];

        if (fatalError is not null)
            errors.Add(fatalError);

        if (_sourceLagProbe is not null) {
            try {
                await _sourceLagProbe.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                errors.Add(ex);
            }
        }

        try {
            await _checkpointer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        try {
            await _subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        if (errors.Count > 0) {
            Status = DroneStatus.Error;

            var aex = new AggregateException(
                fatalError is not null
                    ? "Fatal error in processor drone"
                    : "Errors during graceful shutdown",
                errors).Flatten();

            _telemetry.Error(aex);

            if (fatalError is not null)
                throw aex;
        }
        else {
            Status = DroneStatus.Stopped;
        }

        _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed);
    }

    #endregion
}
