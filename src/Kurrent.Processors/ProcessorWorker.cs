using System.Collections.Immutable;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;

namespace Kurrent.Processors;

sealed class ProcessorWorker : IProcessorWorker {
    readonly ProcessorSettings          _settings;
    readonly KurrentClient              _client;
    readonly IProcessorGateway          _gateway;
    readonly TimeProvider               _timeProvider;
    readonly WorkerTelemetry            _telemetry;
    readonly PartitionedPositionTracker _tracker;
    readonly IRecordProcessor           _processor;
    readonly bool                       _ownsClient;
    readonly ProgressTracker            _progress;

    int _disposed;
    int _started;

    WorkerContext WorkerContext { get; set; }

    public ProcessorWorker(
        ProcessorSettings settings,
        KurrentClient     client,
        IRecordProcessor  processor,
        ILoggerFactory    loggerFactory,
        TimeProvider      timeProvider,
        bool              ownsClient
    ) : this(
        settings,
        client,
        new SimpleGateway(client),
        processor,
        loggerFactory,
        timeProvider,
        ownsClient) { }

    internal ProcessorWorker(
        ProcessorSettings settings,
        KurrentClient     client,
        IProcessorGateway gateway,
        IRecordProcessor  processor,
        ILoggerFactory    loggerFactory,
        TimeProvider      timeProvider,
        bool              ownsClient
    ) {
        _settings   = settings;
        _client     = client;
        _processor  = new SafeRecordProcessor(processor);
        _ownsClient = ownsClient && !client.Shared;

        _gateway      = gateway;
        _timeProvider = timeProvider;

        _progress = new ProgressTracker(timeProvider);
        _tracker  = new PartitionedPositionTracker();

        var logger = loggerFactory.CreateLogger<ProcessorWorker>();

        _telemetry = new WorkerTelemetry(logger, settings.ProcessorId, settings.WorkerId);

        WorkerContext = new WorkerContext {
            ProcessorId = ProcessorId,
            WorkerId    = WorkerId,
            SessionId   = SessionId.Unset,
            Logger      = logger
        };
    }

    public string            ProcessorId => _settings.ProcessorId;
    public string            WorkerId    => _settings.WorkerId;
    public ProcessorSettings Settings    => _settings;

    /// <summary>
    /// Current lifecycle status of the processor worker.
    /// </summary>
    public ProcessorWorkerStatus Status { get; private set; } = ProcessorWorkerStatus.Starting;

    /// <summary>
    /// Current progress snapshot.
    /// </summary>
    public WorkerProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException("This worker has already been started at least once. Create a new instance to run again.");

        Status = ProcessorWorkerStatus.Starting;

        _telemetry.Starting();

        // used to cancel pump/timer before shutdown logic
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        WorkerContext = WorkerContext with {
            CancellationToken = stoppingToken
        };

        try {
            await foreach (var message in _gateway.ConnectAsync(_settings, cancellator.Token).ConfigureAwait(false)) {
                await message
                    .SwitchAsync(
                        HandleWorkOrder,
                        HandleRecord,
                        HandleCheckpoint
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) {
            await HandleStopped(cancellator, ex).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) {
            await HandleError(cancellator, ex).ConfigureAwait(false);
            return;
        }

        await HandleStopped(cancellator).ConfigureAwait(false);
    }

    async ValueTask HandleWorkOrder(WorkOrder workOrder) {
        _telemetry.RestoringState(workOrder.Positions.Length);

        var positions = workOrder.Positions
            .ToImmutableDictionary(p => p.Partition, p => p);

        _tracker.Reset(positions);
        _progress.SetPartitionTracker(_tracker);

        var lowWatermark = positions.Count > 0 ? positions.Values.Min() : RecordPosition.Earliest;

        _telemetry.StateRestored(lowWatermark.LogPosition, positions.Count);

        WorkerContext = WorkerContext with { SessionId = workOrder.SessionId };

        await _gateway
            .SendAsync(new WorkerReady(PartitionPositions.From(positions)))
            .ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Processing;

        _telemetry.Processing(workOrder.SessionId);
    }

    async ValueTask HandleRecord(Record record) {
        using var scope = _telemetry.TrackRecord();

        var ctx = WorkerContext.ForProcessRecord(Progress, record);

        await _processor
            .ProcessRecord(ctx)
            .ConfigureAwait(false);

        if (ctx.StopProcessingRequest.Trigger == StopProcessingTrigger.StopBeforeTrack) {
            var cex = new OperationCanceledException(
                $"Before tracking record by user request. Reason: "
              + $"{ctx.StopProcessingRequest.Reason}");

            scope.Fail(cex);
            throw cex;
        }

        _tracker.Track(record);
        _progress.TrackRecord();

        if (ctx.StopProcessingRequest.Trigger == StopProcessingTrigger.StopAfterTrack) {
            var cex = new OperationCanceledException(
                $"After tracking record by user request. Reason: "
              + $"{ctx.StopProcessingRequest.Reason}");

            scope.Fail(cex);
            throw cex;
        }

        scope.Complete(Progress.Throughput.TotalRecords);
        _telemetry.RecordProcessed(Progress.Throughput.TotalRecords);
    }

    // async ValueTask HandleCheckpoint(Checkpoint checkpoint, CancellationToken ct) {
    //     _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);
    //
    //     var pendingFlush = _tracker.PrepareFlush();
    //
    //     await _processor
    //         .OnCheckpoint(WorkerContext.ForCheckpoint(pendingFlush.Positions, checkpoint))
    //         .ConfigureAwait(false);
    //
    //     await _gateway
    //         .SendAsync(new CheckpointComplete(checkpoint.CheckpointId, pendingFlush.Positions))
    //         .ConfigureAwait(false);
    //
    //     pendingFlush.Execute();
    //
    //     _progress.TrackCheckpoint(checkpoint);
    //     _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, pendingFlush);
    // }

    async ValueTask HandleCheckpoint(Checkpoint checkpoint) {
        _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

        var pendingFlush = _tracker.PrepareFlush();

        PartitionPositions positions = pendingFlush.Positions
            .Where(x => x.Value.LogPosition != LogPosition.Earliest)
            .ToImmutableDictionary();

        try {
            var ctx = WorkerContext.ForCheckpoint(positions, checkpoint);

            await _processor
                .OnCheckpoint(ctx)
                .ConfigureAwait(false);

            await _gateway
                .SendAsync(new CheckpointComplete(checkpoint.CheckpointId, positions))
                .ConfigureAwait(false);

            pendingFlush.Execute();

            _progress.TrackCheckpoint(checkpoint);
            _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, positions, ex);

            if (_settings.Checkpoint.FailurePolicy == CheckpointFailurePolicy.FailProcessor || _settings.Debug)
                throw;
        }
    }

    async ValueTask HandleStopped(CancellationTokenSource cts, OperationCanceledException? cex = null) {
        // Kill pump and timer immediately (idempotent if already canceled by stoppingToken)
        await cts.CancelAsync().ConfigureAwait(false);

        var progress   = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(
            throughput.TotalRecords,
            throughput.Elapsed,
            throughput.RecordsPerSecond);

        List<Exception> errors = [];

        #region ->> Checkpointing <<-

        // because the user handler needs this id as well as every fucking thing...
        var checkpoint = new Checkpoint(
            CheckpointId.New(),
            CheckpointTrigger.Shutdown,
            _timeProvider.GetUtcNow()
        );

        _telemetry.CheckpointInitiated(checkpoint.CheckpointId, checkpoint.Trigger);

        var pendingFlush = _tracker.PrepareFlush();

        PartitionPositions positions = pendingFlush.Positions
            .Where(x => x.Value.LogPosition != LogPosition.Earliest)
            .ToImmutableDictionary();

        try {
            var ctx = WorkerContext.ForCheckpoint(positions, checkpoint);

            await _processor
                .OnCheckpoint(ctx)
                .ConfigureAwait(false);

            await _gateway
                .SendAsync(new CheckpointComplete(checkpoint.CheckpointId, positions))
                .ConfigureAwait(false);

            try {
                await _gateway
                    .SendAsync(new Disconnect(DisconnectReason.Stopped, positions))
                    .ConfigureAwait(false);
            }
            catch(Exception disconnectEx) {
                errors.Add(new Exception("Failed to send disconnect message", disconnectEx));
            }

            pendingFlush.Execute();

            _progress.TrackCheckpoint(checkpoint);
            _telemetry.CheckpointComplete(checkpoint.CheckpointId, checkpoint.Trigger, positions);
        }
        catch (Exception ex) {
            _telemetry.CheckpointFailed(checkpoint.CheckpointId, checkpoint.Trigger, positions, ex);

            if (_settings.Checkpoint.FailurePolicy == CheckpointFailurePolicy.FailProcessor || _settings.Debug)
                throw;
        }

        #endregion

        try {
            await _gateway
                .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.Empty))
                .ConfigureAwait(false);
        }
        catch(Exception disconnectEx) {
            errors.Add(new Exception("Failed to send disconnect message", disconnectEx));
        }

        try {
            await _processor
                .OnComplete(WorkerContext.ForComplete(Progress))
                .ConfigureAwait(false);
        } catch (Exception onCompleteEx) {
            errors.Add(onCompleteEx);
        }

        #region ->> Cleanup <<-

        try {
            await _gateway.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception gatewayEx) {
            errors.Add(new Exception("Error detected during comms gateway disposal", gatewayEx));
        }

        try {
            _tracker.Dispose();
        }
        catch (Exception trackerEx) {
            errors.Add(new Exception("Error detected during position tracker disposal", trackerEx));
        }

        try {
            if (_ownsClient)
                await _client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception clientEx) {
            errors.Add(new Exception("Error detected during client disposal", clientEx));
        }

        #endregion

        if (errors.Count > 0) {
            Status = ProcessorWorkerStatus.Error;

            var aex = new AggregateException("Errors detected during graceful shutdown", errors).Flatten();

            _telemetry.Error(aex);

            throw aex;
        }

        Status = ProcessorWorkerStatus.Stopped;

        _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed, cex?.Message);
    }

    async ValueTask HandleError(CancellationTokenSource cts, Exception ex) {
        // Kill pump and timer immediately
        await cts.CancelAsync().ConfigureAwait(false);

        List<Exception> errors = [ex];

        var progress = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(
            throughput.TotalRecords, throughput.Elapsed,
            throughput.RecordsPerSecond
        );

        try {
            await _processor
                .OnError(WorkerContext.ForError(progress, ex))
                .ConfigureAwait(false);
        }
        catch (Exception onErrorEx) {
            errors.Add(onErrorEx);
        }

        // Send Disconnect (best-effort)
        try {
            await _gateway
                .SendAsync(new Disconnect(DisconnectReason.Error, PartitionPositions.Empty))
                .ConfigureAwait(false);
        }
        catch(Exception disconnectEx) {
            errors.Add(new Exception("Failed to send disconnect message", disconnectEx));
        }

        #region ->> Cleanup <<-

        try {
            await _gateway.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception gatewayEx) {
            errors.Add(new Exception("Error detected during comms gateway disposal", gatewayEx));
        }

        try {
            _tracker.Dispose();
        }
        catch (Exception trackerEx) {
            errors.Add(new Exception("Error detected during position tracker disposal", trackerEx));
        }

        try {
            if (_ownsClient)
                await _client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception clientEx) {
            errors.Add(new Exception("Error detected during client disposal", clientEx));
        }

        #endregion

        Status = ProcessorWorkerStatus.Error;

        var aex = new AggregateException("Errors detected during violent shutdown", errors).Flatten();

        _telemetry.Error(aex);

        throw aex;
    }

    //   async ValueTask HandleStopped(CancellationTokenSource cts, OperationCanceledException? cex = null) {
    //     // Kill pump and timer immediately (idempotent if already canceled by stoppingToken)
    //     await cts.CancelAsync().ConfigureAwait(false);
    //
    //     List<Exception> errors = [];
    //
    //     var progress   = Progress;
    //     var throughput = progress.Throughput;
    //
    //     _telemetry.Stopping(
    //         throughput.TotalRecords,
    //         throughput.Elapsed,
    //         throughput.RecordsPerSecond);
    //
    //     // Final checkpoint flush (best-effort)
    //     // Only attempt if we reached Processing state (partitions assigned, tracker initialized)
    //     if (Status == ProcessorWorkerStatus.Processing) {
    //         try {
    //             var pendingFlush = _tracker.PrepareFlush();
    //             var checkpointId = CheckpointId.New();
    //
    //             _telemetry.FinalCheckpoint(pendingFlush.Positions.Count);
    //
    //             var checkpoint = new Checkpoint(
    //                 checkpointId,
    //                 CheckpointTrigger.Shutdown,
    //                 DateTimeOffset.UtcNow
    //             );
    //
    //             // CancellationToken.None — cts is cancelled, stoppingToken may be cancelled
    //             await _processor
    //                 .OnCheckpoint((WorkerContext with { CancellationToken = CancellationToken.None }).ForCheckpoint(checkpoint))
    //                 .ConfigureAwait(false);
    //
    //             var cleanPositions = pendingFlush.Positions
    //                 .Where(x => x.Value.LogPosition != LogPosition.Earliest)
    //                 .ToImmutableDictionary();
    //
    //             await _gateway
    //                 .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.From(cleanPositions)))
    //                 .ConfigureAwait(false);
    //
    //             pendingFlush.Execute();
    //
    //             _progress.TrackCheckpoint(checkpoint);
    //         }
    //         catch (Exception ex) {
    //             // Best-effort: positions replay on restart (at-least-once guarantee preserved)
    //             _telemetry.FinalCheckpointFailed(ex);
    //
    //             try {
    //                 await _gateway
    //                     .SendAsync(new Disconnect(DisconnectReason.Error, PartitionPositions.Empty))
    //                     .ConfigureAwait(false);
    //             }
    //             catch(Exception disconnectEx) {
    //                 errors.Add(new Exception(
    //                                "Failed to send disconnect message. ", disconnectEx));
    //             }
    //         }
    //     }
    //     else {
    //         try {
    //             await _gateway
    //                 .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.Empty))
    //                 .ConfigureAwait(false);
    //         }
    //         catch(Exception disconnectEx) {
    //             errors.Add(new Exception("Failed to send disconnect message.", disconnectEx));
    //         }
    //     }
    //
    //     try {
    //         await _processor
    //             .OnComplete(WorkerContext.ForComplete(Progress))
    //             .ConfigureAwait(false);
    //     } catch (Exception onCompleteEx) {
    //         errors.Add(onCompleteEx);
    //     }
    //
    //     #region ->> Cleanup <<-
    //
    //     try {
    //         await _gateway.DisposeAsync().ConfigureAwait(false);
    //     }
    //     catch (Exception gatewayEx) {
    //         errors.Add(new Exception("Error detected during comms gateway disposal", gatewayEx));
    //     }
    //
    //     try {
    //         _tracker.Dispose();
    //     }
    //     catch (Exception trackerEx) {
    //         errors.Add(new Exception("Error detected during position tracker disposal", trackerEx));
    //     }
    //
    //     try {
    //         if (_ownsClient)
    //             await _client.DisposeAsync().ConfigureAwait(false);
    //     }
    //     catch (Exception clientEx) {
    //         errors.Add(new Exception("Error detected during client disposal", clientEx));
    //     }
    //
    //     #endregion
    //
    //     if (errors.Count > 0) {
    //         Status = ProcessorWorkerStatus.Error;
    //         var aex = new AggregateException("Errors detected during gracefull shutdown", errors).Flatten();
    //         _telemetry.Error(aex);
    //     }
    //
    //     Status = ProcessorWorkerStatus.Stopped;
    //
    //     _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed, cex?.Message);
    // }


    // async ValueTask Cleanup() {
    //     if (Interlocked.Exchange(ref _disposed, 1) == 1)
    //         return;
    //
    //     var errors = new List<Exception>();
    //
    //     try {
    //         await _gateway.DisposeAsync().ConfigureAwait(false);
    //     }
    //     catch (Exception ex) {
    //         errors.Add(ex);
    //     }
    //
    //     try {
    //         _tracker.Dispose();
    //     }
    //     catch (Exception ex) {
    //         errors.Add(ex);
    //     }
    //
    //     try {
    //         if (_ownsClient)
    //             await _client.DisposeAsync().ConfigureAwait(false);
    //     }
    //     catch (Exception ex) {
    //         errors.Add(ex);
    //     }
    //
    //     if (errors.Count > 0) {
    //         var aex = new AggregateException("Errors detected during cleanup phase", errors);
    //         _telemetry.Error(aex);
    //     }
    // }
}

/// <summary>
/// Represents a running processor worker instance.
/// </summary>
[PublicAPI]
public interface IProcessorWorker {
    /// <summary>
    /// Unique identifier of the processor being executed.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// The unique identifier of this worker instance.
    /// Could have been automatically generated if not provided in settings.
    /// </summary>
    string WorkerId { get; }

    /// <summary>
    /// Current lifecycle status of the processor worker.
    /// </summary>
    ProcessorWorkerStatus Status { get; }

    /// <summary>
    /// The configuration settings used by this processor worker.
    /// </summary>
    ProcessorSettings Settings { get; }

    /// <summary>
    /// Runs the processor worker asynchronously until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Token to request cancellation of processing.</param>
    Task RunAsync(CancellationToken stoppingToken);
}

/// <summary>
/// Defines the lifecycle status of a distributed processor worker.
/// </summary>
[PublicAPI]
public enum ProcessorWorkerStatus {
    /// <summary>
    /// Fallback value for serialization and protocol compatibility.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Worker is bootstrapping: connecting, restoring state, and preparing to process.
    /// </summary>
    Starting = 1,

    /// <summary>
    /// Actively processing records.
    /// </summary>
    Processing = 2,

    /// <summary>
    /// Terminal status for a clean shutdown or cancellation.
    /// </summary>
    Stopped = 3,

    /// <summary>
    /// Terminal status for an unrecoverable error.
    /// </summary>
    Error = 4
}
