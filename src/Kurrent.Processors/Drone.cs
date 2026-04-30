using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors;

sealed class Drone : IDrone {
    readonly ISubscriptionSource _source;
    readonly IProcessor          _processor;
    readonly DroneTelemetry      _telemetry;
    readonly ProgressTracker     _progress;
    readonly ILogger             _logger;

    int _started;

    DroneContext DroneContext { get; set; }

    public Drone(
        ProcessorOptions options,
        ISubscriptionSource source,
        IProcessor processor,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider
    ) {
        Options   = options;

        _source    = source;

        _processor = processor switch {
            IRecordBatchProcessor bp => new SafeRecordBatchProcessor(bp),
            IRecordProcessor      rp => new SafeRecordProcessor(rp),
            _ => throw new ArgumentException(
                $"Processor must implement {nameof(IRecordProcessor)} or {nameof(IRecordBatchProcessor)}.",
                nameof(processor))
        };

        _progress  = new ProgressTracker(timeProvider);
        _logger    = loggerFactory.CreateLogger<Drone>();

        _telemetry = new DroneTelemetry(_logger, options.ProcessorId, options.DroneId);

        DroneContext = new DroneContext {
            ProcessorId = ProcessorId,
            DroneId     = DroneId,
            SessionId   = SessionId.Unset,
            Logger      = _logger
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

        try {
            switch (_processor) {
                case IRecordBatchProcessor batchProcessor:
                    await RunBatched(batchProcessor, stoppingToken).ConfigureAwait(false);
                    break;
                case IRecordProcessor recordProcessor:
                    await RunPerRecord(recordProcessor, stoppingToken).ConfigureAwait(false);
                    break;
            }

            await Shutdown().ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            await Shutdown().ConfigureAwait(false);
        }
        catch (Exception ex) {
            await Shutdown(ex).ConfigureAwait(false);
        }
    }

    async Task RunPerRecord(IRecordProcessor processor, CancellationToken stoppingToken) {
        var stopping = false;

        await foreach (var msg in _source.Subscribe(stoppingToken).ConfigureAwait(false)) {
            // Once stop has been signaled, queued Records are dropped — only checkpoints
            // (the Shutdown marker plus any Interval/Manual already in flight) still dispatch.
            if (stopping && msg.Value is Record) continue;

            var result = await msg
                .MatchAsync(
                    HandleConnected,
                    record => HandleRecord(processor, record),
                    HandleCheckpoint
                )
                .ConfigureAwait(false);

            if (result.ShouldStop && !stopping) {
                stopping = true;
                await _source.InjectCheckpoint(CheckpointTrigger.Shutdown, result.Reason).ConfigureAwait(false);
            }
        }
    }

    async Task RunBatched(IRecordBatchProcessor processor, CancellationToken stoppingToken) {
        // Cancellation is propagated to the source via its own _cts (linked to stoppingToken)
        // and translated into a Shutdown marker in the channel. ReadBatched is given None so
        // its outer-loop cancellation guard cannot short-circuit before the source's drain
        // pattern delivers in-flight Interval / Shutdown markers to HandleCheckpoint.
        var stream = _source.Subscribe(stoppingToken)
            .ReadBatched<SubscribeMessage, Record, BatchedSubscribeMessage>(
                processor.MaxBatchSize, processor.BatchWindow,
                classify: static msg => msg.Match<BatchAction<Record, BatchedSubscribeMessage>>(
                    connected  => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(connected),
                    record     => BatchAction<Record, BatchedSubscribeMessage>.Batch(record),
                    checkpoint => BatchAction<Record, BatchedSubscribeMessage>.FlushThenYield(checkpoint)
                ),
                batchToOutput: static records => RecordBatch.From(records),
                CancellationToken.None
            );

        var stopping = false;

        await foreach (var output in stream.ConfigureAwait(false)) {
            // Once stop has been signaled, queued RecordBatches are dropped — only
            // checkpoints (the Shutdown marker plus any Interval already in flight)
            // still dispatch.
            if (stopping && output.Value is RecordBatch) continue;

            var result = await output
                .MatchAsync(
                    HandleConnected,
                    batch => HandleBatch(processor, batch),
                    HandleCheckpoint
                )
                .ConfigureAwait(false);

            if (result.ShouldStop && !stopping) {
                stopping = true;
                await _source.InjectCheckpoint(CheckpointTrigger.Shutdown, result.Reason).ConfigureAwait(false);
            }
        }
    }

    #region ->> Message Handlers <<-

    async ValueTask<RecordProcessingResult> HandleConnected(Connected connected) {
        DroneContext = DroneContext with { SessionId = connected.SessionId };
        Status = DroneStatus.Processing;
        _telemetry.Processing(connected.SessionId);
        await _processor.OnConnected(DroneContext.ForConnected()).ConfigureAwait(false);
        return RecordProcessingResult.Continue;
    }

    async ValueTask<RecordProcessingResult> HandleRecord(IRecordProcessor processor, Record record) {
        using var scope = _telemetry.TrackRecord();

        var autoCheckpoint = Options.Checkpoint.AutoCheckpoint;

        // Partition already calculated by the source before yielding.
        // Track is always valid; the tracker is idempotent if both user and framework
        // track the same position.
        var ctx = DroneContext.ForProcessRecord(
            Progress, record,
            requestTrack: _source.Track);

        await processor
            .ProcessRecord(ctx)
            .ConfigureAwait(false);

        // Auto-track unless StopProcessing was called. The user is signaling they want
        // to take control of this record's tracking — Track() before StopProcessing()
        // claims it; StopProcessing() alone leaves it unclaimed.
        if (autoCheckpoint && !ctx.StopRequested) {
            _source.Track(record);
            _progress.TrackRecord(record.Position, record.Position.Partition);
        }

        // Manual checkpoint requested via ctx.Checkpoint(reason): inject AFTER auto-track
        // so the snapshot at injection time reflects this record's position.
        if (ctx.CheckpointRequested)
            await _source.InjectCheckpoint(CheckpointTrigger.Manual, ctx.CheckpointReason).ConfigureAwait(false);

        scope.Complete(Progress.Throughput.TotalRecords);

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
    }

    async ValueTask<RecordProcessingResult> HandleBatch(IRecordBatchProcessor processor, RecordBatch batch) {
        var ctx = DroneContext.ForBatch(Progress, batch);

        await processor
            .ProcessBatch(ctx)
            .ConfigureAwait(false);

        // Track positions only after the batch dispatch returns successfully — source-side
        // commit cadence is naturally aligned with batch boundaries. The source's batch
        // Track is atomic with InjectCheckpoint's snapshot, so a concurrent Shutdown
        // injection cannot capture mid-batch state.
        _source.Track(batch);
        foreach (var record in batch) {
            using var scope = _telemetry.TrackRecord();
            _progress.TrackRecord(record.Position, record.Position.Partition);
            scope.Complete(Progress.Throughput.TotalRecords);
        }

        return ctx.StopRequested
            ? RecordProcessingResult.Stop(ctx.StopReason)
            : RecordProcessingResult.Continue;
    }

    async ValueTask<RecordProcessingResult> HandleCheckpoint(Checkpoint checkpoint) {
        _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

        // Shutdown commits must complete even when the orchestrating cancellation has fired,
        // so the user's OnCheckpoint hook (and the subsequent receipt) see a non-cancellable
        // token. Mirrors the prior CompleteCheckpointAsync(..., None) behavior.
        var checkpointToken = checkpoint.Trigger == CheckpointTrigger.Shutdown
            ? CancellationToken.None
            : DroneContext.CancellationToken;

        var ctx = DroneContext.ForCheckpoint(checkpoint.Positions, checkpoint) with {
            CancellationToken = checkpointToken
        };

        Exception? hookEx = null;

        try {
            await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
        }
        catch (Exception ex) {
            hookEx = ex;
        }

        if (hookEx is not null) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, checkpoint.Positions, hookEx);

            // Debug rethrows so test failures surface; production lets FailurePolicy decide.
            if (Options.Debug)
                throw hookEx;

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw hookEx;

            return RecordProcessingResult.Continue;
        }

        try {
            await _source.CompleteCheckpoint(checkpoint, checkpointToken).ConfigureAwait(false);

            _progress.TrackCheckpoint(checkpoint, checkpoint.Positions);
            _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, checkpoint.Positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, checkpoint.Positions, ex);

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;
        }

        return RecordProcessingResult.Continue;
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

        if (fatalError is null) {
            try {
                await _processor
                    .OnComplete(DroneContext.ForComplete(Progress))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (!Options.Debug) {
                errors.Add(ex);
            }
        }

        try {
            await _source.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(new Exception("Error disposing subscription source", ex));
        }

        if (errors.Count > 0) {
            Status = DroneStatus.Error;

            var aex = new AggregateException(
                fatalError is not null
                    ? "Fatal error in processor drone"
                    : "Errors during graceful shutdown",
                errors
            ).Flatten();

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

    public static RecordProcessingResult Stop(string? reason) => new(true, reason);
}
