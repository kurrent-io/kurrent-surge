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
    readonly WorkerTelemetry     _telemetry;
    readonly ProgressTracker     _progress;
    readonly ILogger             _logger;

    int _started;

    WorkerContext WorkerContext { get; set; }

    public Drone(
        ProcessorSettings settings,
        ISubscriptionSource source,
        IRecordProcessor processor,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider
    ) {
        Settings   = settings;
        _source    = source;
        _processor = new SafeRecordProcessor(processor);

        _progress = new ProgressTracker(timeProvider);

        _logger = loggerFactory.CreateLogger<Drone>();

        _telemetry = new WorkerTelemetry(_logger, settings.ProcessorId, settings.WorkerId);

        WorkerContext = new WorkerContext {
            ProcessorId = ProcessorId,
            WorkerId    = WorkerId,
            SessionId   = SessionId.Unset,
            Logger      = _logger
        };
    }

    public string ProcessorId => Settings.ProcessorId;
    public string WorkerId    => Settings.WorkerId;

    public ProcessorSettings Settings { get; }

    public ProcessorWorkerStatus Status { get; private set; } = ProcessorWorkerStatus.Starting;

    public WorkerProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This drone has already been started. Create a new instance to run again.");

        Status = ProcessorWorkerStatus.Starting;

        _telemetry.Starting();

        WorkerContext = WorkerContext with { CancellationToken = stoppingToken };

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

    ValueTask HandleConnected(Connected connected) {
        WorkerContext = WorkerContext with { SessionId = connected.SessionId };
        Status = ProcessorWorkerStatus.Processing;
        _telemetry.Processing(connected.SessionId);
        return ValueTask.CompletedTask;
    }

    async ValueTask HandleRecord(Record record) {
        using var scope = _telemetry.TrackRecord();

        // Partition already calculated by the source before yielding
        var ctx = WorkerContext.ForProcessRecord(Progress, record);

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
                var ctx = WorkerContext.ForCheckpoint(checkpoint.Positions, checkpoint);
                await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (!Settings.Debug) {
                _logger.LogWarning(ex, "[{ProcessorId}/{WorkerId}] User OnCheckpoint handler failed", ProcessorId, WorkerId);
            }

            // 2. Complete — source persists and advances tracker
            var positions = await _source.CompleteCheckpointAsync(checkpoint.CheckpointId).ConfigureAwait(false);

            _progress.TrackCheckpoint(checkpoint, positions);
            _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, checkpoint.Positions, ex);

            if (Settings.Checkpoint.FailurePolicy == CheckpointFailurePolicy.FailProcessor)
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

        if (Status == ProcessorWorkerStatus.Processing) {
            var checkpoint = _source.CreateShutdownCheckpoint();

            _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

            // User OnCheckpoint — best-effort in production, throws in Debug
            try {
                var ctx = WorkerContext.ForCheckpoint(checkpoint.Positions, checkpoint);
                await _processor.OnCheckpoint(ctx).ConfigureAwait(false);
            }
            catch (Exception ex) when (!Settings.Debug) {
                _logger.LogWarning(ex, "[{ProcessorId}/{WorkerId}] User OnCheckpoint handler failed during shutdown", ProcessorId, WorkerId);
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
                .OnComplete(WorkerContext.ForComplete(Progress))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!Settings.Debug) {
            errors.Add(ex);
        }

        await Cleanup(errors).ConfigureAwait(false);

        // Final status — checkpoint policy can escalate to error
        if (checkpointFailed && Settings.Checkpoint.FailurePolicy == CheckpointFailurePolicy.FailProcessor) {
            Status = ProcessorWorkerStatus.Error;

            var aex = new AggregateException("Checkpoint failed during shutdown", errors).Flatten();
            _telemetry.Error(aex);
            throw aex;
        }

        if (errors.Count > 0) {
            Status = ProcessorWorkerStatus.Error;

            var aex = new AggregateException("Errors during graceful shutdown", errors).Flatten();
            _telemetry.Error(aex);
        }
        else {
            Status = ProcessorWorkerStatus.Stopped;
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
                .OnError(WorkerContext.ForError(progress, ex))
                .ConfigureAwait(false);
        }
        catch (Exception onErrorEx) {
            errors.Add(onErrorEx);
        }

        await Cleanup(errors).ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Error;

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
