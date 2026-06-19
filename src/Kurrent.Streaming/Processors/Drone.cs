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

sealed class Drone : IDrone {
    readonly ISubscription      _subscription;
    readonly IProcessor         _processor;
    readonly Checkpointer       _checkpointer;
    readonly StreamsClient      _streams;
    readonly DroneTelemetry     _telemetry;
    readonly ProgressTracker    _progress;
    readonly HybridLogicalClock _clock;
    readonly TimeProvider       _timeProvider;

    readonly Channel<SubscribeMessage> _channel;

    SourceLagProbe? _sourceLagProbe;

    int _started;

    DroneContext DroneContext { get; set; }

    public Drone(
        ProcessorOptions options,
        ISubscription subscription,
        Checkpointer checkpointer,
        Channel<SubscribeMessage> channel,
        StreamsClient streams,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IProcessor processor
    ) {
        Options = options;

        _subscription = subscription;
        _checkpointer = checkpointer;
        _channel      = channel;
        _streams      = streams;
        _clock        = new HybridLogicalClock(timeProvider);
        _timeProvider = timeProvider;

        _processor = processor switch {
            IRecordBatchProcessor bp => new SafeRecordBatchProcessor(bp),
            IRecordProcessor rp      => new SafeRecordProcessor(rp),
            _ => throw new ArgumentException(
                $"Processor must implement {nameof(IRecordProcessor)} or {nameof(IRecordBatchProcessor)}.",
                nameof(processor))
        };

        _progress = new ProgressTracker(timeProvider, options.MaxParallelism);

        var logger = loggerFactory.CreateLogger<Drone>();

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

        // Channel closed — subscription ended, the user stopped, or a processing failure faulted the loop.
        // If the user stopped, the Shutdown barrier already committed pending positions.
        // If the subscription ended naturally, pending positions may remain.
        //
        // Run the final barrier on a clean exit OR a ProcessBatch failure: a processing failure must still
        // flush the frontier the user transparently tracked before throwing — OnCheckpoint runs and those
        // positions commit — instead of dropping the user's durable intent and replaying the whole batch.
        // Do NOT run it on an OnCheckpoint failure: the hook just threw, so re-running it would only fault
        // again. The discriminator is the PROCESS_BATCH_FAILED code carried by the wrapping DroneException.
        var isProcessFailure = fatalError is DroneException { ErrorDetails.ErrorCode.Value: "PROCESS_BATCH_FAILED" };

        if ((fatalError is null || isProcessFailure) && _checkpointer.HasPendingPositions) {
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
        // options, not the processor — the only branch below is how a flushed batch is dispatched.
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

                case RecordBatch batch:
                    switch (_processor) {
                        case IRecordBatchProcessor bp: {
                            var result = await HandleBatch(bp, batch, catchUp).ConfigureAwait(false);

                            if (result.ShouldStop && !stopping) {
                                stopping = true;

                                await _checkpointer
                                    .RaiseCheckpoint(CheckpointTrigger.Shutdown, result.Reason)
                                    .ConfigureAwait(false);
                            }

                            break;
                        }

                        case IRecordProcessor rp:
                            await DispatchPerRecord(rp, batch).ConfigureAwait(false);
                            break;
                    }

                    break;

                case Checkpoint checkpoint:
                    await HandleCheckpoint(checkpoint, partitions).ConfigureAwait(false);
                    break;
            }
        }

        // Inner stop scope: closure over `stopping` so a record requesting stop can break out of its
        // batch and close the outer scope. The catch-up filter runs BEFORE dispatch — a caught-up
        // record never reaches ProcessRecord (intentionally unlike the batch path, which delivers the
        // whole batch and filters only its count/track loop).
        async ValueTask DispatchPerRecord(IRecordProcessor processor, RecordBatch batch) {
            foreach (var record in batch) {
                if (catchUp.TryGetValue(record.Position.Partition, out var cp)
                 && record.Position.LogPosition <= cp)
                    continue;

                var result = await HandleRecord(processor, record).ConfigureAwait(false);

                if (result.ShouldStop) {
                    stopping = true;

                    await _checkpointer
                        .RaiseCheckpoint(CheckpointTrigger.Shutdown, result.Reason)
                        .ConfigureAwait(false);

                    break; // skip the remaining records in this batch
                }
            }
        }
    }

    async ValueTask<RecordProcessingResult> HandleRecord(IRecordProcessor processor, Record record) {
        using var scope = _telemetry.TrackRecord();

        var autoTrack = Options.Checkpoint.AutoTrack;

        var ctx = DroneContext.ForProcessRecord(
            Progress, record,
            requestTrack: _checkpointer.Track);

        await processor
            .ProcessRecord(ctx)
            .ConfigureAwait(false);

        // Count every dispatched record toward throughput, independent of tracking mode.
        _progress.CountThroughput(record);

        if (autoTrack && !ctx.StopRequested) {
            _checkpointer.Track(record.Position);
            _progress.TrackPosition(record.Position);
        }
        else if (ctx.TrackedByUser) {
            // Manual track (auto-track off, or a stop record the user explicitly claimed): the
            // checkpointer was already advanced by ctx.Track; mirror the position into progress.
            _progress.TrackPosition(record.Position);
        }

        // Stop supersedes a pending manual checkpoint: once a stop is requested the graceful Shutdown
        // barrier is the single OnCheckpoint + commit point, so raising a Manual barrier here would
        // double-commit. ctx.Checkpoint() already threw if a stop was requested before it.
        if (ctx.CheckpointRequested && !ctx.StopRequested)
            await _checkpointer
                .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                .ConfigureAwait(false);

        // Cheap scalar reads — never materialize the full DroneProgress snapshot on the per-record path.
        scope.Complete(_progress.TotalRecords, _progress.UncommittedRecords, _progress.UncommittedBytes);

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
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

        // Stop supersedes a pending manual checkpoint: once a stop is requested the graceful Shutdown
        // barrier is the single OnCheckpoint + commit point, so raising a Manual barrier here would
        // double-commit. ctx.Checkpoint() already threw if a stop was requested before it.
        if (ctx.CheckpointRequested && !ctx.StopRequested)
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

/// <summary>
/// Outcome of dispatching a single channel message to the drone's handlers.
/// </summary>
readonly record struct RecordProcessingResult(bool ShouldStop, string? Reason) {
    public static readonly RecordProcessingResult Continue = new(false, null);

    public static RecordProcessingResult Stop(string? reason) =>
        new(true, reason);
}