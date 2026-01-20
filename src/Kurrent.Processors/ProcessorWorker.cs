using System.Collections.Immutable;
using Kurrent.Client.Processors.Checkpointing;
using Kurrent.Client.Processors.Gateway;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams.Processing;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Streams;

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
    /// Performing local bootstrap (DI, configuration, and environment validation).
    /// </summary>
    Initializing = 1,

    /// <summary>
    /// Connecting/handshaking with the server to establish identity and request a <see cref="WorkOrder"/>.
    /// </summary>
    Connecting = 2,

    /// <summary>
    /// <see cref="WorkOrder"/> received.
    /// Hydrating local state and seeking to the specified <see cref="LogPosition"/> before signaling readiness.
    /// </summary>
    Restoring = 3,

    /// <summary>
    /// Actively processing.
    /// Handshake complete (<see cref="WorkerReady"/> sent) and records are being consumed.
    /// </summary>
    Processing = 4,

    /// <summary>
    /// Graceful teardown in progress. Releasing partition leases and flushing
    /// outbound buffers to the Relay.
    /// </summary>
    Disconnecting = 5,

    /// <summary>
    /// Terminal status for a clean shutdown.
    /// All network sessions and local resources have been released.
    /// </summary>
    Disconnected = 6,

    /// <summary>
    /// Terminal status for an unrecoverable error.
    /// Partitions should be considered abandoned and eligible for reassignment.
    /// </summary>
    Failed = 7
}

/// <summary>
/// Represents a running processor worker instance.
/// </summary>
[PublicAPI]
public interface IProcessorWorker : IAsyncDisposable {
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

sealed partial class ProcessorWorker : IProcessorWorker {
    readonly KurrentClient     _client;
    readonly ProcessorSettings _settings;
    readonly IProcessorGateway _gateway;
    readonly ILogger           _logger;
    readonly CheckpointStore   _checkpointStore;

    readonly PartitionedPositionTracker _tracker;

    readonly IRecordProcessor _processor;
    readonly bool             _ownsClient;
    readonly ProgressTracker  _progress;

    readonly IStateStore _state;

    IWorkerSession? _session;

    public ProcessorWorker(KurrentClient client, IRecordProcessor processor, IStateStore state, ProcessorSettings settings, ILoggerFactory loggerFactory, bool ownsClient = true) {
        _client     = client;
        _processor  = processor;
        _state      = state;
        _logger     = loggerFactory.CreateLogger(processor.GetType().Name);
        _ownsClient = ownsClient && !client.Shared;

        _settings = settings;
        Status   = ProcessorWorkerStatus.Initializing;

        _gateway = new LegacyGateway(_client);

        _checkpointStore = new CheckpointStore(
            _settings.ProcessorId,
            _settings.MaxParallelism,
            _client.Streams,
            TimeProvider.System,
            loggerFactory.CreateLogger<CheckpointStore>());

        _progress = new ProgressTracker(TimeProvider.System);
        _tracker  = new PartitionedPositionTracker(ImmutableDictionary<Partition, RecordPosition>.Empty);

        // _state = _client.Processors.CreateStateStore(_settings.ProcessorId, _settings.WorkerId);
    }

    public string ProcessorId           => _settings.ProcessorId;
    public string WorkerId              => _settings.WorkerId;
    public ProcessorSettings Settings   => _settings;

    SessionId _sessionId = SessionId.None;

    public ProcessorWorkerStatus Status   { get; private set; }

    /// <summary>
    /// Current progress snapshot.
    /// </summary>
    public ProcessorProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        LogInitializing(ProcessorId, WorkerId);

        var ctx = new InitializationContext(_settings, _logger, stoppingToken);
        await _processor.OnInitialize(ctx).ConfigureAwait(false);

        try {
            Status = ProcessorWorkerStatus.Connecting;
            LogConnecting(ProcessorId, WorkerId);

            _session = await _gateway.ConnectAsync(_settings, stoppingToken).ConfigureAwait(false);

            _sessionId = _session.SessionId;
            LogConnected(ProcessorId, WorkerId, _session.SessionId.Value);

            await foreach (var message in _session.WithCancellation(stoppingToken).ConfigureAwait(false)) {
                await message
                    .SwitchAsync(
                        evt => HandleWorkOrder(evt, stoppingToken),
                        evt => HandleRecordBatch(evt, stoppingToken),
                        evt => HandleCheckpointBarrier(evt, stoppingToken)
                    )
                    .ConfigureAwait(false);
            }

            Status = ProcessorWorkerStatus.Disconnecting;
            await HandleShutdown().ConfigureAwait(false);
        }
        catch (Exception ex) {
            Status = ProcessorWorkerStatus.Disconnecting;
            await HandleShutdown(ex).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() {
        _tracker.Dispose();

        if (_session is not null)
            await _session.DisposeAsync().ConfigureAwait(false);

        await _gateway.DisposeAsync().ConfigureAwait(false);

        if (_ownsClient)
            await _client.DisposeAsync().ConfigureAwait(false);
    }

    async ValueTask HandleWorkOrder(WorkOrder workOrder, CancellationToken ct) {
        Status = ProcessorWorkerStatus.Restoring;
        LogWorkOrderReceived(ProcessorId, WorkerId, workOrder.Partitions.Length);

        var checkpoint = await _checkpointStore
            .LoadLatestAsync(ct)
            .ConfigureAwait(false);

        var (startFrom, positions) = checkpoint.ResolveStartPosition(
            _settings.Subscription.InitialPosition,
            _settings.Subscription.StartPositions
        );

        _tracker.Reset(positions);
        _progress.SetPartitionTracker(_tracker);

        // Generate or use barrier ID from checkpoint
        var barrierId = checkpoint.BarrierId.IsEmpty ? BarrierId.New() : checkpoint.BarrierId;

        LogStateRestored(ProcessorId, WorkerId, startFrom, positions.Count);

        await _session!
            .SendAsync(new WorkerReady(barrierId, PartitionPositions.From(positions)))
            .ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Processing;

        LogProcessing(ProcessorId, WorkerId);
    }

    async ValueTask HandleRecordBatch(RecordBatch batch, CancellationToken ct) {
        await _processor
            .ProcessRecords(new ProcessRecordsContext(ProcessorId, WorkerId, _sessionId, batch, Progress, _state, _logger, ct))
            .ConfigureAwait(false);

        _tracker.Track(batch);
        _progress.TrackRecords(batch.Count);

        LogBatchProcessed(ProcessorId, WorkerId, batch.Count, Progress.Throughput.TotalRecords);
    }

    async ValueTask HandleCheckpointBarrier(CheckpointBarrier barrier, CancellationToken ct) {
        LogCheckpointBarrierReceived(ProcessorId, WorkerId, barrier.Trigger, barrier.BarrierId);

        var pendingFlush = _tracker.PrepareFlush();

        await _checkpointStore
            .StoreAsync(WorkerId, barrier.BarrierId, pendingFlush.Positions, ct)
            .ConfigureAwait(false);

        await _session!
            .SendAsync(new CheckpointComplete(barrier.BarrierId, PartitionPositions.From(pendingFlush.Positions)))
            .ConfigureAwait(false);

        pendingFlush.Execute();
        _progress.TrackCheckpoint(barrier);

        LogCheckpointCompleted(ProcessorId, WorkerId, barrier.BarrierId, pendingFlush.Positions.Count);

        await _processor
            .OnCheckpointComplete(new CheckpointCompleteContext(ProcessorId, WorkerId, _sessionId, Progress, ct))
            .ConfigureAwait(false);
    }

    async ValueTask HandleShutdown(Exception? ex = null) {
        Status = ex is null ? ProcessorWorkerStatus.Disconnected : ProcessorWorkerStatus.Failed;

        // capture final snapshot
        var progress = Progress;

        if (ex is null)
            LogShutdownCompleted(ProcessorId, WorkerId, progress.Throughput.TotalRecords, progress.Throughput.Elapsed);
        else
            LogShutdownFailed(ex, ProcessorId, WorkerId);

        await _processor
            .OnShutdown(ShutdownContext.From(ProcessorId, WorkerId, _sessionId, _logger, progress, ex))
            .ConfigureAwait(false);
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} initializing")]
    partial void LogInitializing(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} connecting")]
    partial void LogConnecting(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} connected (sessionId: {SessionId})")]
    partial void LogConnected(string processorId, string workerId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} received work order ({PartitionCount} partitions), restoring state")]
    partial void LogWorkOrderReceived(string processorId, string workerId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} restored state (startPosition: {StartPosition}, partitions: {PartitionCount})")]
    partial void LogStateRestored(string processorId, string workerId, LogPosition startPosition, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} ready and processing")]
    partial void LogProcessing(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processor {ProcessorId} worker {WorkerId} processed batch ({RecordCount} records, total: {TotalRecords})")]
    partial void LogBatchProcessed(string processorId, string workerId, int recordCount, long totalRecords);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} checkpoint barrier (trigger: {Trigger}, barrierId: {BarrierId})")]
    partial void LogCheckpointBarrierReceived(string processorId, string workerId, CheckpointBarrierTrigger trigger, string barrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} checkpoint completed (barrierId: {BarrierId}, partitions: {PartitionCount})")]
    partial void LogCheckpointCompleted(string processorId, string workerId, string barrierId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} shutdown completed ({TotalRecords} records in {Duration})")]
    partial void LogShutdownCompleted(string processorId, string workerId, long totalRecords, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor {ProcessorId} worker {WorkerId} shutdown with error")]
    partial void LogShutdownFailed(Exception ex, string processorId, string workerId);

    #endregion
}
