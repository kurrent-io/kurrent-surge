using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors;

sealed class Drone : IDrone {
    readonly ISubscriptionSource _source;
    readonly IRecordProcessor    _processor;
    readonly DroneTelemetry      _telemetry;
    readonly ProgressTracker     _progress;
    readonly ILogger             _logger;

    int _started;

    DroneContext DroneContext { get; set; }

    public Drone(
        ProcessorOptions options,
        ISubscriptionSource source,
        IRecordProcessor processor,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider
    ) {
        Options   = options;
        
        _source    = source;
        
        _processor = new SafeRecordProcessor(processor);
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
            await foreach (var msg in _source.Subscribe(stoppingToken).ConfigureAwait(false)) {
                await msg
                    .SwitchAsync(
                        HandleConnected,
                        HandleRecord,
                        HandleCheckpoint
                    )
                    .ConfigureAwait(false);
            }

            await HandleStopped(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) {
            await HandleStopped(stoppingToken, ex).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await HandleError(ex).ConfigureAwait(false);
        }
    }

    #region ->> Message Handlers <<-

    async ValueTask HandleConnected(Connected connected) {
        DroneContext = DroneContext with { SessionId = connected.SessionId };
        Status = DroneStatus.Processing;
        _telemetry.Processing(connected.SessionId);
        await _processor.OnConnected(DroneContext.ForConnected()).ConfigureAwait(false);
    }

    async ValueTask HandleRecord(Record record) {
        using var scope = _telemetry.TrackRecord();

        // Partition already calculated by the source before yielding
        var ctx = DroneContext.ForProcessRecord(
            Progress, record, 
            requestTrackRecord: () =>  _source.Track(record), 
            requestCheckpoint:   _ => throw new NotSupportedException("Checkpointing from the record handler is not yet supported. Checkpointing is only supported from the checkpoint handler."));

        await _processor
            .ProcessRecord(ctx)
            .ConfigureAwait(false);

        if (ctx.StopProcessingRequest.Trigger == StopProcessingTrigger.StopBeforeTrack) {
            var cex = new OperationCanceledException(
                $"Before tracking record by user request. Reason: {ctx.StopProcessingRequest.Reason}");

            scope.Fail(cex);
            throw cex;
        }

        _source.Track(record);
        _progress.TrackRecord(record.Position, record.Position.Partition);

        if (ctx.StopProcessingRequest.Trigger == StopProcessingTrigger.StopAfterTrack) {
            var cex = new OperationCanceledException(
                $"After tracking record by user request. Reason: {ctx.StopProcessingRequest.Reason}");

            scope.Fail(cex);
            throw cex;
        }

        scope.Complete(Progress.Throughput.TotalRecords);
    }

    async ValueTask HandleCheckpoint(Checkpoint checkpoint) {
        _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

        try {
            // 1. User hook — positions come from the message, snapshotted at yield time
            try {
                var ctx = DroneContext.ForCheckpoint(checkpoint.Positions, checkpoint);
                await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (!Options.Debug) {
                _logger.LogWarning(ex, "[{ProcessorId}/{DroneId}] User OnCheckpoint handler failed", ProcessorId, DroneId);
            }

            // 2. Complete — source persists and advances tracker
            var positions = await _source.CompleteCheckpointAsync(checkpoint.CheckpointId).ConfigureAwait(false);

            _progress.TrackCheckpoint(checkpoint, positions);
            _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, checkpoint.Positions, ex);

            if (Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail)
                throw;
        }
    }

    #endregion

    #region ->> Stop Flows <<-

    async ValueTask HandleStopped(CancellationToken stoppingToken, OperationCanceledException? cex = null) {
        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        List<Exception> errors = [];

        var checkpointFailed = false;

        #region ->> Checkpoint <<-

        if (Status == DroneStatus.Processing) {
            var checkpoint = _source.CreateShutdownCheckpoint();

            _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

            // User OnCheckpoint — best-effort in production, throws in Debug
            try {
                var ctx = DroneContext.ForCheckpoint(checkpoint.Positions, checkpoint);
                await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (!Options.Debug) {
                _logger.LogWarning(ex, "[{ProcessorId}/{DroneId}] User OnCheckpoint handler failed during shutdown", ProcessorId, DroneId);
            }

            // Persist — best-effort, independent of user handler
            try {
                var positions = await _source
                    .CompleteCheckpointAsync(checkpoint.CheckpointId, CancellationToken.None)
                    .ConfigureAwait(false);

                _progress.TrackCheckpoint(checkpoint, positions);
                _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
            }
            catch (Exception ex) {
                checkpointFailed = true;
                _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, checkpoint.Positions, ex);
                errors.Add(ex);
            }
        }

        #endregion

        // User OnComplete — best-effort in production, throws in Debug
        try {
            await _processor
                .OnComplete(DroneContext.ForComplete(Progress))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!Options.Debug) {
            errors.Add(ex);
        }

        await Cleanup(errors).ConfigureAwait(false);

        // Final status — checkpoint policy can escalate to error
        if (checkpointFailed && Options.Checkpoint.FailurePolicy == CheckpointFailurePolicy.Fail) {
            Status = DroneStatus.Error;

            var aex = new AggregateException("Checkpoint failed during shutdown", errors).Flatten();
            _telemetry.Error(aex);
            throw aex;
        }

        if (errors.Count > 0) {
            Status = DroneStatus.Error;

            var aex = new AggregateException("Errors during graceful shutdown", errors).Flatten();
            _telemetry.Error(aex);
        }
        else {
            Status = DroneStatus.Stopped;
        }

        _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed, cex?.Message);
    }

    async ValueTask HandleError(Exception ex) {
        List<Exception> errors = [ex];

        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(throughput.TotalRecords, throughput.Elapsed, throughput.RecordsPerSecond);

        // User OnError — always guarded, even in Debug mode.
        // If the error handler itself fails, both exceptions surface in the aggregate.
        try {
            await _processor
                .OnError(DroneContext.ForError(progress, ex))
                .ConfigureAwait(false);
        }
        catch (Exception onErrorEx) {
            errors.Add(onErrorEx);
        }

        await Cleanup(errors).ConfigureAwait(false);

        Status = DroneStatus.Error;

        var aex = new AggregateException("Fatal error in processor drone", errors).Flatten();
        _telemetry.Error(aex);
        throw aex;
    }

    #endregion

    #region ->> Internals <<-

    async ValueTask Cleanup(List<Exception> errors) {
        try {
            await _source.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(new Exception("Error disposing subscription source", ex));
        }
    }

    #endregion
}
