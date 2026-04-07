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

        _gateway = gateway;

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
                        msg => HandleWorkOrder(msg, stoppingToken),
                        msg => HandleRecord(msg, stoppingToken),
                        msg => HandleCheckpoint(msg, stoppingToken)
                    )
                    .ConfigureAwait(false);
            }

            await HandleStopped(cancellator).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) {
            await HandleStopped(cancellator, ex).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await HandleError(cancellator, ex).ConfigureAwait(false);
        }
        finally {
            await Cleanup().ConfigureAwait(false);
        }
    }

    async ValueTask HandleWorkOrder(WorkOrder workOrder, CancellationToken ct) {
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

    async ValueTask HandleRecord(Record record, CancellationToken ct) {
        using var scope = _telemetry.TrackRecord();

        var ctx = WorkerContext.ForProcessRecord(record, Progress);

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

    async ValueTask HandleCheckpoint(Checkpoint checkpoint, CancellationToken ct) {
        _telemetry.Checkpointing(checkpoint.Trigger, checkpoint.CheckpointId);

        var pendingFlush = _tracker.PrepareFlush();

        await _processor
            .OnCheckpoint(WorkerContext.ForCheckpoint(checkpoint))
            .ConfigureAwait(false);

        await _gateway
            .SendAsync(new CheckpointComplete(checkpoint.CheckpointId, PartitionPositions.From(pendingFlush.Positions)))
            .ConfigureAwait(false);

        pendingFlush.Execute();

        _progress.TrackCheckpoint(checkpoint);
        _telemetry.CheckpointComplete(checkpoint.Trigger, checkpoint.CheckpointId, pendingFlush);
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

        // Final checkpoint flush (best-effort)
        // Only attempt if we reached Processing state (partitions assigned, tracker initialized)
        if (Status == ProcessorWorkerStatus.Processing) {
            try {
                var pendingFlush = _tracker.PrepareFlush();
                var checkpointId = CheckpointId.New();

                _telemetry.FinalCheckpoint(pendingFlush.Positions.Count);

                var checkpoint = new Checkpoint(
                    checkpointId,
                    CheckpointTrigger.Shutdown,
                    DateTimeOffset.UtcNow
                );

                // CancellationToken.None — cts is cancelled, stoppingToken may be cancelled
                await _processor
                    .OnCheckpoint((WorkerContext with { CancellationToken = CancellationToken.None }).ForCheckpoint(checkpoint))
                    .ConfigureAwait(false);

                var cleanPositions = pendingFlush.Positions
                    .Where(x => x.Value.LogPosition != LogPosition.Earliest).ToImmutableDictionary();

                await _gateway
                    .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.From(cleanPositions)))
                    .ConfigureAwait(false);

                pendingFlush.Execute();

                _progress.TrackCheckpoint(checkpoint);
            }
            catch (Exception ex) {
                // Best-effort: positions replay on restart (at-least-once guarantee preserved)
                _telemetry.FinalCheckpointFailed(ex);

                // Still send disconnect so gateway can clean up
                try {
                    await _gateway
                        .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.Empty))
                        .ConfigureAwait(false);
                }
                catch {
                    // Swallow
                }
            }
        }
        else {
            // Send Disconnect (best-effort, no-op if gateway not started)
            try {
                await _gateway
                    .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.Empty))
                    .ConfigureAwait(false);
            }
            catch {
                // Swallow — protocol signaling, not critical
            }
        }

        // OnComplete callback (throws on failure, preserving current behavior)
        var ctx = WorkerContext.ForComplete(Progress);

        try {
            await _processor
                .OnComplete(ctx)
                .ConfigureAwait(false);

            Status = ProcessorWorkerStatus.Stopped;
            _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed, cex?.Message);

            ctx.Logger.LogInformation("*** Process stopped gracefully ***");
        } catch (Exception completeEx) {
            Status = ProcessorWorkerStatus.Error;
            _telemetry.Error(completeEx);

            ctx.Logger.LogInformation("***Process stopped due to exception ***");
            throw;
        }
    }

    async ValueTask HandleError(CancellationTokenSource cts, Exception ex) {
        // Kill pump and timer immediately
        await cts.CancelAsync().ConfigureAwait(false);

        var progress = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(
            throughput.TotalRecords, throughput.Elapsed,
            throughput.RecordsPerSecond
        );

        // OnError callback — aggregate on failure
        try {
            await _processor
                .OnError(WorkerContext.ForError(progress, ex))
                .ConfigureAwait(false);
        }
        catch (Exception uex) {
            ex = new AggregateException(ex, uex).Flatten();
        }

        // Send Disconnect (best-effort)
        try {
            await _gateway
                .SendAsync(new Disconnect(DisconnectReason.Error, PartitionPositions.Empty))
                .ConfigureAwait(false);
        }
        catch {
            // Swallow — protocol signaling, not critical
        }

        Status = ProcessorWorkerStatus.Error;
        _telemetry.Error(ex);
        throw ex;
    }

    async ValueTask Cleanup() {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        var errors = new List<Exception>();

        try {
            await _gateway.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        try {
            _tracker.Dispose();
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        try {
            if (_ownsClient)
                await _client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            errors.Add(ex);
        }

        if (errors.Count > 0) {
            var aggregate = new AggregateException("One or more errors during processor cleanup", errors);
            _telemetry.Error(aggregate);
            throw aggregate;
        }
    }
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
