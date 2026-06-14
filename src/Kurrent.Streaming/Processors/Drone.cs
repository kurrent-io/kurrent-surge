using System.Diagnostics;
using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Diagnostics;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Options;

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

        _progress = new ProgressTracker(timeProvider);

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
            _sourceLagProbe = new SourceLagProbe(
                readHead: ct => _streams.InspectRecord(LogPosition.Latest, skipDecoding: true, ct),
                snapshot: () => Progress,
                onSample: _telemetry.SourceLagSample,
                timeProvider: _timeProvider);

            _sourceLagProbe.Start(stoppingToken);
        }

        Exception? fatalError = null;

        try {
            switch (_processor) {
                case IRecordBatchProcessor batchProcessor:
                    await RunBatched(batchProcessor, catchUp).ConfigureAwait(false); break;

                case IRecordProcessor recordProcessor:
                    await RunPerRecord(recordProcessor, catchUp).ConfigureAwait(false); break;
            }
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
            Checkpoint checkpoint = new ShutdownCheckpoint(new CheckpointId(now), now.DateTimeOffset);

            await HandleCheckpoint(checkpoint, partitions).ConfigureAwait(false);
        }

        await Shutdown(fatalError).ConfigureAwait(false);
    }

    async Task RunPerRecord(IRecordProcessor processor, Dictionary<Partition, LogPosition> catchUp) {
        // The drone's owned partitions — the broadcast target for server watermarks (see HandleCheckpoint).
        var partitions = catchUp.Keys.ToArray();
        var stopping   = false;

        await foreach (var msg in _channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false)) {
            if (stopping && msg.Value is Record)
                continue;

            switch (msg.Value) {
                case Connected c: await HandleConnected(c).ConfigureAwait(false); break;

                case Record record: {
                    if (catchUp.TryGetValue(record.Position.Partition, out var cp)
                     && record.Position.LogPosition <= cp)
                        continue;

                    var result = await HandleRecord(processor, record).ConfigureAwait(false);

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
                _progress.TrackPosition(record.Position, record.Position.Partition);
            }
            else if (ctx.TrackedByUser) {
                // Manual track (auto-track off, or a stop record the user explicitly claimed): the
                // checkpointer was already advanced by ctx.Track; mirror the position into progress.
                _progress.TrackPosition(record.Position, record.Position.Partition);
            }

            if (ctx.CheckpointRequested)
                await _checkpointer
                    .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                    .ConfigureAwait(false);

            var progress = Progress;
            scope.Complete(progress.Throughput.TotalRecords, progress.UncommittedRecords, progress.UncommittedBytes);

            return ctx.StopRequested
                ? RecordProcessingResult.Stop(ctx.StopReason)
                : RecordProcessingResult.Continue;
        }
    }

    async Task RunBatched(IRecordBatchProcessor processor, Dictionary<Partition, LogPosition> catchUp) {
        // The drone's owned partitions — the broadcast target for server watermarks (see HandleCheckpoint).
        var partitions = catchUp.Keys.ToArray();

        var stream = _channel.Reader
            .ReadAllAsync(CancellationToken.None)
            .ReadBatched<SubscribeMessage, Record, BatchedSubscribeMessage>(
                processor.MaxBatchSize, processor.BatchWindow,
                classify: static msg => msg.Match<BatchAction<Record, BatchedSubscribeMessage>>(
                    static connected  => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(connected),
                    static record     => BatchAction<Record, BatchedSubscribeMessage>.Batch(record),
                    static checkpoint => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(checkpoint)),
                batchToOutput: static records => RecordBatch.From(records),
                CancellationToken.None);

        var stopping = false;

        await foreach (var output in stream.ConfigureAwait(false)) {
            
            // If stopping, skip any remaining batches but continue to drain checkpoint messages
            // to ensure we can checkpoint final positions and shut down cleanly.
            if (stopping && output.Value is RecordBatch)
                continue;

            switch (output.Value) {
                case Connected c: await HandleConnected(c).ConfigureAwait(false); break;

                case RecordBatch batch: {
                    var result = await HandleBatch(processor, batch, catchUp).ConfigureAwait(false);

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
                    _progress.TrackPosition(record.Position, record.Position.Partition);
                }

                var progress = Progress;
                scope.Complete(progress.Throughput.TotalRecords, progress.UncommittedRecords, progress.UncommittedBytes);
            }

            if (ctx.CheckpointRequested)
                await _checkpointer
                    .RaiseCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason)
                    .ConfigureAwait(false);

            return ctx.StopRequested
                ? RecordProcessingResult.Stop(ctx.StopReason)
                : RecordProcessingResult.Continue;
        }
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
        var trigger        = checkpoint.TriggerKind;
        var startTimestamp = Stopwatch.GetTimestamp();

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
            _telemetry.CheckpointComplete(checkpoint.Id, trigger, positions, Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds);
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