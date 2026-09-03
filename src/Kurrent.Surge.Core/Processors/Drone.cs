using System.Threading.Channels;
using Kurrent.Surge.Client;
using Kurrent.Surge.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Checkpointing;
using Kurrent.Surge.Processors.Configuration;
using Kurrent.Surge.Processors.Checkpointing;
using Kurrent.Surge.Processors.Diagnostics;
using Kurrent.Toolkit.Diagnostics;
using Kurrent.Surge.Processors.Gateway;

namespace Kurrent.Surge.Processors;

sealed class Drone : IDrone {
    readonly IDataSource         _dataSource;
    readonly ProcessorDispatcher _dispatcher;
    readonly ObserverDispatcher  _observers;
    readonly ProcessorInfo       _info;
    readonly Checkpointer        _checkpointer;
    readonly IStreamsClient      _streams;
    readonly ProgressTracker     _progress;
    readonly HybridLogicalClock  _clock;
    readonly TimeProvider        _timeProvider;

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
        IProcessor processor,
        IEnumerable<IProcessorObserver> observers
    ) {
        Options = options;

        _dataSource   = dataSource;
        _checkpointer = checkpointer;
        _channel      = channel;
        _streams      = streams;
        _clock        = new(timeProvider);
        _timeProvider = timeProvider;

        _progress = new(timeProvider, options.MaxParallelism);

        var logger = loggerFactory.CreateLogger<Drone>();

        _observers = new(observers, logger);
        _info      = new(options.ProcessorId, options.DroneId);

        // The composition root: the single point where the processor shape is discriminated. Each
        // dispatcher owns its shape's bookkeeping end-to-end, so the run loop below stays branch-free.
        // Progress and observers must already be constructed — the dispatchers take them.
        _dispatcher = processor switch {
            IRecordBatchProcessor bp => new BatchDispatcher(bp, checkpointer, _progress, _observers, options, timeProvider),
            IRecordProcessor rp      => new RecordDispatcher(rp, checkpointer, _progress, _observers, options, timeProvider),
            _ => throw new ArgumentException($"Processor must implement {nameof(IRecordProcessor)} or {nameof(IRecordBatchProcessor)}.", nameof(processor))
        };

        DroneContext = new() {
            ProcessorId    = ProcessorId,
            DroneId        = DroneId,
            SessionId      = SessionId.Unset,
            Logger         = logger,
            ProgressSource = () => _progress.Progress
        };
    }

    public string ProcessorId => Options.ProcessorId;
    public string DroneId     => Options.DroneId;

    public ProcessorOptions Options { get; }

    public DroneStatus Status { get; private set; } = DroneStatus.Starting;

    /// <summary>
    /// Moves the drone to <paramref name="status"/> and raises the transition on the observer seam.
    /// </summary>
    /// <remarks>
    /// The seam is the single place lifecycle status is published, so status and notification cannot
    /// drift apart. A subscription re-session raises Processing → Processing carrying the new session;
    /// reconnects are lifecycle facts, not a separate event.
    /// </remarks>
    void TransitionTo(DroneStatus status, Exception? error = null, SessionId sessionId = default) {
        var from = Status;

        Status = status;

        _observers.StateChanged(
            new(_info, from, status, _progress.Progress, _timeProvider.GetUtcNow(), error, sessionId));
    }

    public DroneProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        TransitionTo(DroneStatus.Starting);

        DroneContext = DroneContext with { CancellationToken = stoppingToken };

        var partitions = Enumerable
            .Range(0, Options.MaxParallelism)
            .Select(Partition.From)
            .ToArray();

        // The one phase that runs before the start position exists. A processor that keeps its own
        // checkpoints — a projection storing them beside its projected state — resolves them here and
        // claims them, because by the time the subscription opens the position is already settled.
        var initialize = DroneContext.ForInitialize(
            partitions, Options.Subscription.InitialPosition, Options.Subscription.StartPosition);

        await _dispatcher.OnInitialize(initialize).ConfigureAwait(false);

        // The checkpointer resolves the start: load committed, fold the initial-position fallback across owned
        // partitions, reset the store when an explicit start overrides, and seed the tracker. The explicit start
        // is a log position only — the runtime orders by log position and the client fills stream/revision
        // currency lazily at subscribe for stream-targeted filters, so nothing to complete here.
        // A checkpoint owner has already done all of that against its own storage, including honouring the
        // explicit start; the checkpointer takes its positions and never touches the store.
        RecordPosition[] startPositions;

        if (initialize.OwnedCheckpoints is { } owned) {
            _checkpointer.InitializeOwned(owned, stoppingToken);
            startPositions = owned;
        }
        else {
            startPositions = await _checkpointer
                .Initialize(partitions, Options.Subscription.InitialPosition, Options.Subscription.StartPosition, stoppingToken)
                .ConfigureAwait(false);
        }

        // Hand the resume point to progress as well: it is the baseline every at-risk measurement is
        // taken from, and without it nothing at risk is measurable until the first checkpoint.
        _progress.Initialize(startPositions);

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
                onSample: (lagBytes, lagSeconds) =>
                    _observers.SourceLagSampled(
                        new(_info, lagBytes, lagSeconds, _timeProvider.GetUtcNow())),
                timeProvider: _timeProvider,
                refreshInterval: Options.SourceLagRefreshInterval);

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

                case RecordBatch batch: {
                    var result = await _dispatcher.Dispatch(DroneContext, batch).ConfigureAwait(false);

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

    #region ->> Message Handlers <<-

    async ValueTask<RecordProcessingResult> HandleConnected(Connected connected) {
        DroneContext = DroneContext with { SessionId = connected.SessionId };

        TransitionTo(DroneStatus.Processing, sessionId: connected.SessionId);


        await _dispatcher
            .OnConnected(DroneContext.ForConnected())
            .ConfigureAwait(false);
        
        return RecordProcessingResult.Continue;
    }
    
    async ValueTask HandleCheckpoint(Checkpoint checkpoint, Partition[] partitions) {
        var trigger = checkpoint.TriggerKind;

        // Timed from here so CheckpointCommitted.Duration spans the whole barrier — the handler and the
        // commit. That span is why a separate "initiated" event was never worth carrying.
        var start = _timeProvider.GetTimestamp();

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
            await _dispatcher.OnCheckpoint(ctx).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _observers.CheckpointFailed(
                new(_info, trigger, checkpoint.Id, pending, ex, _timeProvider.GetUtcNow()));

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

            _progress.TrackCheckpoint(positions);

            _observers.CheckpointCommitted(
                new(
                    _info, trigger, checkpoint.Id, positions,
                    _timeProvider.GetElapsedTime(start), _timeProvider.GetUtcNow()));
        }
        catch (Exception ex) {
            _observers.CheckpointFailed(
                new(_info, trigger, checkpoint.Id, positions, ex, _timeProvider.GetUtcNow()));

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
        // Run the final barrier on a clean exit OR a processing failure: a processing failure must still
        // flush the frontier the user transparently tracked before throwing — OnCheckpoint runs and those
        // positions commit — instead of dropping the user's durable intent and replaying the whole batch.
        // Both dispatcher shapes qualify; the record path tracks a prefix before throwing exactly as the
        // batch path does. Do NOT run it on an OnCheckpoint failure: the hook just threw, so re-running it
        // would only fault again. The discriminator is the code carried by the wrapping DroneException.
        var isProcessFailure = fatalError is DroneException {
            ErrorDetails.ErrorCode.Value: "PROCESS_BATCH_FAILED" or "PROCESS_RECORD_FAILED"
        };

        if ((fatalError is null || isProcessFailure) && _checkpointer.HasPendingPositions) {
            var now = _clock.Now();
            var checkpoint = new ShutdownCheckpoint(new(now), now.DateTimeOffset);
            await HandleCheckpoint(checkpoint, partitions).ConfigureAwait(false);
        }

        // The final barrier has run, so this transition carries the run's settled throughput.
        TransitionTo(DroneStatus.Stopping);

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

        // After the checkpointer, so a processor that owns its checkpoints still has whatever it opened
        // when the final barrier committed through it.
        try {
            await _dispatcher.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        if (errors.Count > 0) {
            var aex = new AggregateException(
                fatalError is not null
                    ? "Fatal error in processor drone"
                    : "Errors during graceful shutdown",
                errors).Flatten();

            // Stopped either way — the error rides on the transition, matching Surge. Callers tell a
            // crash from a graceful stop by the presence of ProcessorStateChanged.Error.
            TransitionTo(DroneStatus.Stopped, aex);

            if (fatalError is not null)
                throw aex;
        }
        else {
            TransitionTo(DroneStatus.Stopped);
        }
    }

    #endregion
}