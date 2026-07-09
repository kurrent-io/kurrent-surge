using System.Threading.Channels;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Streaming.Checkpointing;
using Kurrent.Surge.Streaming.Diagnostics;
using Kurrent.Surge.Streaming.Gateway;

namespace Kurrent.Surge.Streaming;

sealed class Drone : IDrone {
    readonly IDataSource        _dataSource;
    readonly IProcessor         _processor;
    readonly Checkpointer       _checkpointer;
    readonly IStreamsClient     _streams;
    readonly DroneTelemetry     _telemetry;
    readonly ProgressTracker    _progress;
    readonly HybridLogicalClock _clock;
    readonly TimeProvider       _timeProvider;

    readonly Channel<DataSourceMessage> _channel;

    SourceLagProbe? _sourceLagProbe;

    int _started;

    DroneContext DroneContext { get; set; }

    public Drone(
        ProcessorOptions options,
        IDataSource dataSource,
        Checkpointer checkpointer,
        Channel<DataSourceMessage> channel,
        IStreamsClient streams,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IProcessor processor
    ) {
        Options = options;

        _dataSource   = dataSource;
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

        // The checkpointer resolves the start: load committed, fold the initial-position fallback across owned
        // partitions, reset the store when an explicit start overrides, and seed the tracker. The explicit start
        // is a log position only — the runtime orders by log position and the client fills stream/revision
        // currency lazily at subscribe for stream-targeted filters, so nothing to complete here.
        var startPositions = await _checkpointer
            .Initialize(partitions, Options.Subscription.InitialPosition, Options.Subscription.StartPosition, stoppingToken)
            .ConfigureAwait(false);

        // The data source is feed-only: it subscribes from the least-advanced start, tags partitions, and prunes
        // the caught-up prefix against the floors these positions carry — so the Run loop below sees only new
        // records. It writes to the channel and completes it when done (faulting it on failure). Never throws.
        _ = _dataSource.Connect(_channel.Writer, startPositions, stoppingToken);

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
            await Run(partitions).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex) {
            fatalError = ex.InnerException;
        }
        catch (Exception ex) {
            fatalError = ex;
        }

        // The final barrier — flush pending positions on a clean exit or a ProcessBatch failure — is part
        // of shutdown now (see Shutdown).
        await Shutdown(fatalError, partitions).ConfigureAwait(false);
    }

    async Task Run(Partition[] partitions) {
        var stopping = false;

        // Single batched read of the channel: records accumulate into a RecordBatch; Connected and
        // Checkpoint markers flush the pending batch and pass through. The data source already pruned the
        // caught-up prefix, so every batch here is new work — no per-partition catch-up filter in this loop.
        var stream = _channel.Reader
            .ReadAllAsync(CancellationToken.None)
            .ReadBatched<DataSourceMessage, Record, BatchedSubscribeMessage>(
                Options.Batching.MaxBatchSize, Options.Batching.BatchWindow,
                classify: static msg => msg.Match<BatchAction<Record, BatchedSubscribeMessage>>(
                    static connected  => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(connected),
                    static record     => BatchAction<Record, BatchedSubscribeMessage>.Batch(record),
                    static checkpoint => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(checkpoint)),
                batchToOutput: static records => RecordBatch.From(records),
                CancellationToken.None);

        await foreach (var output in stream.ConfigureAwait(false)) {
            var value = output.Value;

            // Outer stop scope: once stopping, skip later batches AND server scan watermarks
            // (Filter / CaughtUp). The batched read pulls a trailing watermark in ahead of the stop;
            // folding it would advance the checkpoint past the last dispatched record (matching records
            // beyond the stop were skipped, not processed), so a resuming run would start past its own
            // stop record and hang on a live subscription. Only the injected Shutdown barrier — which
            // commits exactly the tracked positions — is honored after stop.
            if (stopping && value is RecordBatch or FilterCheckpoint or CaughtUpCheckpoint)
                continue;

            switch (value) {
                case Connected c:
                    await HandleConnected(c).ConfigureAwait(false);
                break;

                case RecordBatch batch:
                    switch (_processor) {
                        case IRecordBatchProcessor bp: {
                            var result = await HandleBatch(bp, batch).ConfigureAwait(false);

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
        // batch and close the outer scope.
        async ValueTask DispatchPerRecord(IRecordProcessor processor, RecordBatch batch) {
            foreach (var record in batch) {
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

        // Track is transparent — it advances the checkpointer AND progress together — so a user ctx.Track
        // updates the gauges, not just the commit position (see Track(RecordPosition)).
        var ctx = DroneContext.ForProcessRecord(
            Progress, record,
            requestTrack: Track);

        await processor
            .ProcessRecord(ctx)
            .ConfigureAwait(false);

        // Count every dispatched record toward throughput, independent of tracking mode.
        _progress.CountThroughput(record);

        // Auto-track this record only when AutoTrack is on and no stop was requested; otherwise the user
        // owns the frontier via ctx.Track (already transparent above). Re-tracking the same position is
        // idempotent on progress and harmless on the checkpointer snapshot.
        if (autoTrack && !ctx.StopRequested)
            Track(record.Position);

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

    async ValueTask<RecordProcessingResult> HandleBatch(IRecordBatchProcessor processor, RecordBatch batch) {
        // One scope around the REAL work: times ProcessBatch (not the bookkeeping that follows it).
        using var scope = _telemetry.TrackBatch(batch.Count);

        // Track is transparent (checkpointer + progress together), so a user ctx.Track updates the gauges.
        var ctx = DroneContext.ForBatch(
            Progress, batch,
            requestTrack: Track);

        await processor
            .ProcessBatch(ctx)
            .ConfigureAwait(false);

        // Resolve the committed frontier (the invariant): auto-track the whole-batch frontier (max position
        // per partition) only when AutoTrack is on AND no stop was requested — a stop caps the commit at
        // exactly what the user tracked via ctx.Track*. In every other case the commit is the user's tracks.
        if (Options.Checkpoint.AutoTrack && !ctx.StopRequested)
            Track(batch);

        // Throughput counts every dispatched record (the "batch trap": delivered records count even with
        // AutoTrack off and nothing committed). The batch is pre-pruned in Run, so Count is the dispatched
        // count — advanced in one step, not a per-record loop.
        if (batch.Count > 0)
            _progress.CountThroughput(batch.Count, batch[batch.Count - 1].Timestamp);

        // records_processed is the committed/durable prefix: records at or below the resolved frontier
        // (PendingPositions = the last tracked position per partition). It never over-counts a stop's
        // un-committed remainder, and counts a replay only when it is eventually committed, never twice.
        var processed = CountUpToFrontier(batch, _checkpointer.PendingPositions());

        scope.Complete(processed, _progress.TotalRecords, _progress.UncommittedRecords, _progress.UncommittedBytes);

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

    // The transparent tracking primitive — advances the checkpointer AND progress together. Wired as the
    // batch/record RequestTrack delegate so a user ctx.Track* updates the uncommitted gauges (closing the
    // transparency gap), and used by auto-track so every tracking source folds progress identically.
    void Track(RecordPosition position) {
        _checkpointer.Track(position);
        _progress.TrackPosition(position);
    }

    // Auto-track the whole batch: one pass to the max position per partition, then Track each through the
    // transparent primitive. NOT PartitionedPositionTracker.Track(IEnumerable) — that folds max-per-partition
    // but bypasses _progress, and tracking must fold progress too.
    void Track(RecordBatch batch) {
        Dictionary<Partition, RecordPosition>? frontier = null;

        foreach (var record in batch) {
            var position = record.Position;

            // At most one entry per owned partition, never per record — size to the partition count.
            frontier ??= new Dictionary<Partition, RecordPosition>(Options.MaxParallelism);

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

    async ValueTask Shutdown(Exception? fatalError, Partition[] partitions) {
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

        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        // AutoTrack off + records dispatched + nothing ever committed ⇒ the subscription replays in full on
        // restart. Warn once, here, at the one moment it is provably true — the final barrier (if any) has
        // already run, so a zero checkpoint count means no progress was persisted. No hot-path cost.
        if (!Options.Checkpoint.AutoTrack && _progress.TotalRecords > 0 && _progress.TotalCheckpoints == 0)
            _telemetry.TrackingDisabledNoProgress();

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