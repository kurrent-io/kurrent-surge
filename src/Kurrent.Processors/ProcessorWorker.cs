using System.Collections.Immutable;
using Kurrent.Client;
using Kurrent.Client.Processors.Checkpointing;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Processing;
using Kurrent.Processors.Gateway;

namespace Kurrent.Processors;

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

    SessionId _sessionId = SessionId.None;

    public ProcessorWorker(KurrentClient client, IRecordProcessor processor, IStateStore state, ProcessorSettings settings,  ILoggerFactory loggerFactory, TimeProvider? time = null, bool ownsClient = true) {
        _client     = client;
        _processor  = processor;
        _state      = state;
        _logger     = loggerFactory.CreateLogger(processor.GetType().Name);
        _ownsClient = ownsClient && !client.Shared;

        _settings = settings;

        time ??= TimeProvider.System;

        _gateway = new LegacyGateway(_client);

        _checkpointStore = new CheckpointStore(
            _settings.ProcessorId,
            _settings.MaxParallelism,
            _client.Streams,
            time,
            loggerFactory.CreateLogger<CheckpointStore>());

        _progress = new ProgressTracker(time);
        _tracker  = new PartitionedPositionTracker();
    }

    public string            ProcessorId => _settings.ProcessorId;
    public string            WorkerId    => _settings.WorkerId;
    public ProcessorSettings Settings    => _settings;

    /// <summary>
    /// Current lifecycle status of the processor worker.
    /// </summary>
    public ProcessorWorkerStatus Status { get; private set; }

    /// <summary>
    /// Current progress snapshot.
    /// </summary>
    public ProcessorProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        await HandleInitialization(stoppingToken).ConfigureAwait(false);

        try {
            Status = ProcessorWorkerStatus.Connecting;
            LogConnecting(ProcessorId, WorkerId);

            _session = await _gateway
                .ConnectAsync(_settings, stoppingToken)
                .ConfigureAwait(false);

            _sessionId = _session.SessionId;

            LogConnected(ProcessorId, WorkerId, _session.SessionId.Value);

            await foreach (var message in _session.WithCancellation(stoppingToken).ConfigureAwait(false)) {
                await message
                    .SwitchAsync(
                        msg => HandleWorkOrder(msg, stoppingToken),
                        msg => HandleRecordBatch(msg, stoppingToken),
                        msg => HandleCheckpointBarrier(msg, stoppingToken)
                    )
                    .ConfigureAwait(false);
            }

            await HandleShutdown().ConfigureAwait(false);
        }
        catch (Exception ex) {
            await HandleShutdown(ex).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync() {
        try {
            if (_session is not null)
                await _session.DisposeAsync().ConfigureAwait(false);

            _tracker.Dispose();

            if (_ownsClient || !_client.Shared) // TODO SS: better way to manage shared clients?
                await _client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            LogIrregularShutdown(ex, ProcessorId, WorkerId, ex.Message);
            throw;
        }
    }

    async ValueTask HandleInitialization(CancellationToken ct) {
        Status = ProcessorWorkerStatus.Initializing;

        try {
            LogInitializing(ProcessorId, WorkerId);

            await _processor
                .OnInitialize(new (_settings, _logger, ct))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw new InitializeException(ex);
        }
    }

    async ValueTask HandleWorkOrder(WorkOrder workOrder, CancellationToken ct) {
        Status = ProcessorWorkerStatus.Restoring;

        LogWorkOrderReceived(ProcessorId, WorkerId, workOrder.Partitions.Length);

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

        try {
            await _processor
                .OnPartitionsAssigned(new(ProcessorId, WorkerId, _sessionId, _state, _logger, ct))
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw new PartitionsAssignedException(ex);
        }

        LogStateRestored(ProcessorId, WorkerId, checkpoint.LowWatermark.LogPosition, positions.Count);

        await _session!
            .SendAsync(new WorkerReady(checkpoint.BarrierId, PartitionPositions.From(positions)))
            .ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Processing;

        LogReady(ProcessorId, WorkerId);
    }

    async ValueTask HandleRecordBatch(RecordBatch batch, CancellationToken ct) {
        try {
            await _processor
                .ProcessRecords(
                    new ProcessRecordsContext(
                        ProcessorId, WorkerId, _sessionId,
                        batch, Progress, _state,
                        _logger, ct)
                )
                .ConfigureAwait(false);

            _tracker.Track(batch);
            _progress.TrackRecords(batch.Count);

            LogBatchProcessed(
                ProcessorId, WorkerId, batch.Count,
                Progress.Throughput.TotalRecords
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw new ProcessRecordsException(ex);
        }
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

        LogCheckpointComplete(ProcessorId, WorkerId, barrier.BarrierId, pendingFlush.Positions.Count);

        await _processor
            .OnCheckpointComplete(new (ProcessorId, WorkerId, _sessionId, Progress, barrier, _state, _logger, ct))
            .ConfigureAwait(false);
    }

    async ValueTask HandleShutdown(Exception? ex = null) {
        Status = ProcessorWorkerStatus.Disconnecting;

        LogShutdownInitiated(ProcessorId, WorkerId);

        var progress = Progress;

        try {
            await _processor
                .OnShutdown(new(ProcessorId, WorkerId, _sessionId, progress, _logger, ex))
                .ConfigureAwait(false);
        }
        catch (Exception uex) {
            uex = new Exception($"{ProcessorId} {WorkerId} failed to handle user shutdown: {uex.Message}", uex);

            ex = ex is not null
                ? new AggregateException(ex, uex).Flatten()
                : uex;
        }

        Status = ProcessorWorkerStatus.Disconnected;

        if (ex is null)
            LogShutdown(ProcessorId, WorkerId, progress.Throughput.TotalRecords, progress.Throughput.Elapsed);
        else {
            LogIrregularShutdown(ex, ProcessorId, WorkerId, ex.Message);
            throw ex;
        }
    }

    async ValueTask HandleShutdown2(Exception? ex = null) {
        Status = ProcessorWorkerStatus.Disconnecting;

        LogShutdownInitiated(ProcessorId, WorkerId);

        var progress = Progress;

        try {
            await _processor
                .OnShutdown(new(ProcessorId, WorkerId, _sessionId, progress, _logger, ex))
                .ConfigureAwait(false);
        }
        catch (Exception uex) {
            uex = new Exception($"{ProcessorId} {WorkerId} failed to handle user shutdown: {uex.Message}", uex);

            ex = ex is not null
                ? new AggregateException(ex, uex).Flatten()
                : uex;
        }

        Status = ProcessorWorkerStatus.Disconnected;

        if (ex is null)
            LogShutdown(ProcessorId, WorkerId, progress.Throughput.TotalRecords, progress.Throughput.Elapsed);
        else {
            LogIrregularShutdown(ex, ProcessorId, WorkerId, ex.Message);
            throw ex;
        }
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
    partial void LogReady(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processor {ProcessorId} worker {WorkerId} processed batch ({RecordCount} records, total: {TotalRecords})")]
    partial void LogBatchProcessed(string processorId, string workerId, int recordCount, long totalRecords);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} checkpoint barrier (trigger: {Trigger}, barrierId: {BarrierId})")]
    partial void LogCheckpointBarrierReceived(string processorId, string workerId, CheckpointBarrierTrigger trigger, string barrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} checkpoint completed (barrierId: {BarrierId}, partitions: {PartitionCount})")]
    partial void LogCheckpointComplete(string processorId, string workerId, string barrierId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} shutting down...")]
    partial void LogShutdownInitiated(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} shutdown completed ({TotalRecords} records in {Duration})")]
    partial void LogShutdown(string processorId, string workerId, long totalRecords, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor {ProcessorId} worker {WorkerId} shutdown with error: {Message}")]
    partial void LogIrregularShutdown(Exception ex, string processorId, string workerId, string message);

    #endregion
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
