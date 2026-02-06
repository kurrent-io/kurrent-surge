using System.Collections.Immutable;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Diagnostics;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using CheckpointStore = Kurrent.Processors.Checkpointing.CheckpointStore;

namespace Kurrent.Processors;

sealed class ProcessorWorker : IProcessorWorker {
    readonly ProcessorSettings          _settings;
    readonly KurrentClient              _client;
    readonly IProcessorGateway          _gateway;
    readonly WorkerTelemetry    _telemetry;
    readonly CheckpointStore            _checkpointStore;
    readonly PartitionedPositionTracker _tracker;
    readonly IRecordProcessor           _processor;
    readonly bool                       _ownsClient;
    readonly ProgressTracker            _progress;

    int _disposed;
    int _started;

    WorkerContext WorkerContext;

    public ProcessorWorker(
        ProcessorSettings settings,
        KurrentClient client,
        IRecordProcessor processor,
        WorkerTelemetry telemetry,
        ILoggerFactory loggerFactory,
        TimeProvider? time = null,
        bool ownsClient = true,
        IProcessorGateway? gateway = null
    ) {
        _client     = client;
        _processor  = new SafeRecordProcessor(processor);
        _telemetry  = telemetry;
        _ownsClient = ownsClient && !client.Shared;

        _settings = settings;

        time ??= TimeProvider.System;

        _gateway = gateway ?? new LegacyGateway(_client);

        _checkpointStore = new CheckpointStore(
            _settings.ProcessorId,
            _settings.MaxParallelism,
            _client.Streams,
            time,
            loggerFactory.CreateLogger<CheckpointStore>());

        _progress = new ProgressTracker(time);
        _tracker  = new PartitionedPositionTracker();

        WorkerContext = new WorkerContext {
            ProcessorId = ProcessorId,
            WorkerId    = WorkerId,
            SessionId   = SessionId.None,
            Logger      = loggerFactory.CreateLogger<ProcessorWorker>()
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

        IWorkerSession session = null!;

        // used to cancel pump/timer before shutdown logic
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        WorkerContext = WorkerContext with {
            CancellationToken = stoppingToken
        };

        try {
            session = await _gateway
                .ConnectAsync(_settings, cancellator.Token)
                .ConfigureAwait(false);

            WorkerContext = WorkerContext with {
                SessionId = session.SessionId
            };

            await foreach (var message in session.WithCancellation(stoppingToken).ConfigureAwait(false)) {
                await message
                    .SwitchAsync(
                        msg => HandleWorkOrder(msg, session, stoppingToken),
                        msg => HandleRecordBatch(msg, session, stoppingToken),
                        msg => HandleCheckpoint(msg, session, stoppingToken)
                    )
                    .ConfigureAwait(false);
            }

            await HandleStopped(session, cancellator).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            await HandleStopped(session, cancellator).ConfigureAwait(false);
        }
        catch (Exception ex) {
            await HandleError(session, cancellator, ex).ConfigureAwait(false);
        }
        finally {
            await Cleanup(session).ConfigureAwait(false);
        }
    }

    async ValueTask HandleWorkOrder(WorkOrder workOrder, IWorkerSession session, CancellationToken ct) {
        _telemetry.RestoringState(workOrder.Partitions.Length);

        // Load checkpoint with resolved positions for all assigned partitions
        var checkpoint = await _checkpointStore
            .LoadLatestAsync(
                workOrder.Partitions.ToArray(),
                _settings.Subscription.InitialPosition,
                ct)
            .ConfigureAwait(false);

        // Extract positions for tracker and WorkerReady
        var positions = checkpoint.Partitions
            .ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.Position);

        _tracker.Reset(positions);
        _progress.SetPartitionTracker(_tracker);

        _telemetry.StateRestored(checkpoint.LowWatermark.LogPosition, positions.Count);

        await session!
            .SendAsync(new WorkerReady(checkpoint.CheckpointId, PartitionPositions.From(positions)))
            .ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Processing;

        _telemetry.Processing(session.SessionId.Value);
    }

    async ValueTask HandleRecordBatch(RecordBatch batch, IWorkerSession session, CancellationToken ct) {
        using var scope = _telemetry.TrackBatch();

        await _processor
            .ProcessRecords(WorkerContext.ForProcessRecords(batch, Progress))
            .ConfigureAwait(false);

        _tracker.Track(batch);
        _progress.TrackRecords(batch.Count);

        scope.Complete(batch.Count, Progress.Throughput.TotalRecords);
        _telemetry.BatchProcessed(batch.Count, Progress.Throughput.TotalRecords);
    }

    async ValueTask HandleCheckpoint(Checkpoint checkpoint, IWorkerSession session, CancellationToken ct) {
        _telemetry.Checkpointing(checkpoint.Trigger, checkpoint.CheckpointId);

        var pendingFlush = _tracker.PrepareFlush();

        await _processor
            .OnCheckpoint(WorkerContext.ForCheckpoint(checkpoint))
            .ConfigureAwait(false);

        await _checkpointStore
            .StoreAsync(WorkerId, checkpoint.CheckpointId, pendingFlush.Positions, ct)
            .ConfigureAwait(false);

        await session
            .SendAsync(new CheckpointComplete(checkpoint.CheckpointId, PartitionPositions.From(pendingFlush.Positions)))
            .ConfigureAwait(false);

        pendingFlush.Execute();
        _progress.TrackCheckpoint(checkpoint);

        _telemetry.CheckpointComplete(checkpoint.Trigger, checkpoint.CheckpointId, pendingFlush);
    }

    async ValueTask HandleStopped(IWorkerSession session, CancellationTokenSource cts) {
        // Kill pump and timer immediately (idempotent if already canceled by stoppingToken)
        await cts.CancelAsync().ConfigureAwait(false);

        var progress = Progress;
        var throughput = progress.Throughput;

        _telemetry.Stopping(
            throughput.TotalRecords, throughput.Elapsed,
            throughput.RecordsPerSecond
        );

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
                    .OnCheckpoint(
                        (WorkerContext with { CancellationToken = CancellationToken.None })
                            .ForCheckpoint(checkpoint))
                    .ConfigureAwait(false);

                await _checkpointStore
                    .StoreAsync(WorkerId, checkpointId, pendingFlush.Positions, CancellationToken.None)
                    .ConfigureAwait(false);

                pendingFlush.Execute();
                _progress.TrackCheckpoint(checkpoint);
            }
            catch (Exception ex) {
                // Best-effort: positions replay on restart (at-least-once guarantee preserved)
                _telemetry.FinalCheckpointFailed(ex);
            }
        }

        // Send Disconnect (best-effort, no-op in legacy)
        try {
            await session
                .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.Empty))
                .ConfigureAwait(false);
        }
        catch {
            // Swallow — protocol signaling, not critical
        }

        // OnComplete callback (throws on failure, preserving current behavior)
        try {
            await _processor
                .OnComplete(WorkerContext.ForComplete(Progress))
                .ConfigureAwait(false);

            Status = ProcessorWorkerStatus.Stopped;
            _telemetry.Stopped(throughput.TotalRecords, throughput.Elapsed);
        }
        catch (Exception completeEx) {
            Status = ProcessorWorkerStatus.Error;
            _telemetry.Error(completeEx);
            throw;
        }
    }

    async ValueTask HandleError(IWorkerSession session, CancellationTokenSource cts, Exception ex) {
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

        // Send Disconnect (best-effort, no-op in legacy)
        try {
            await session
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

    async ValueTask Cleanup(IWorkerSession? session) {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        var errors = new List<Exception>();

        try {
            if (session is not null)
                await session.DisposeAsync().ConfigureAwait(false);
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
