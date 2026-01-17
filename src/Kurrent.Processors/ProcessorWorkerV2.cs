using Kurrent.Client.Processors.Checkpointing;
using Kurrent.Client.Processors.Diagnostics;
using Kurrent.Client.Processors.Gateway;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams.Processing;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Streams;

/// <summary>
/// V2: Uses unified ProcessorProgressTracker instead of separate tracker + progress.
/// </summary>
sealed partial class ProcessorWorkerV2 : IProcessorWorker {
    readonly KurrentClient                 _client;
    readonly ProcessorSettings             _settings;
    readonly IProcessorGateway             _gateway;
    readonly ILogger<ProcessorWorkerV2>    _logger;
    readonly CheckpointStore _checkpointStore;
    readonly ProcessorProgressTracker      _progress;  // unified tracker

    readonly IRecordProcessor _processor;
    readonly bool             _ownsClient;

    IWorkerSession? _session;

    public ProcessorWorkerV2(KurrentClient client, IRecordProcessor processor, ProcessorSettings settings, ILoggerFactory loggerFactory) {
        _client     = client;
        _processor  = processor;
        _logger     = loggerFactory.CreateLogger<ProcessorWorkerV2>();
        _ownsClient = !client.Shared;

        _settings = settings;
        Status    = ProcessorWorkerStatus.Initializing;

        _gateway = new LegacyGateway(_client);

        _checkpointStore = new CheckpointStore(
            _settings.ProcessorId,
            _settings.MaxParallelism,
            _client.Streams,
            TimeProvider.System,
            loggerFactory.CreateLogger<CheckpointStore>());

        _progress = new ProcessorProgressTracker(TimeProvider.System);
    }

    public string ProcessorId           => _settings.ProcessorId;
    public string WorkerId              => _settings.WorkerId;
    public ProcessorSettings Settings   => _settings;

    SessionId _sessionId = SessionId.None;

    public ProcessorWorkerStatus Status { get; private set; }

    /// <summary>
    /// Current progress snapshot.
    /// </summary>
    public ProcessorProgress Progress => _progress.Progress;

    public async Task RunAsync(CancellationToken stoppingToken) {
        LogInitializing(ProcessorId, WorkerId);

        var ctx = new InitializationContext(_settings, stoppingToken);
        await _processor.OnInitialize(ctx).ConfigureAwait(false);

        try {
            Status = ProcessorWorkerStatus.Connecting;
            LogConnecting(ProcessorId, WorkerId);

            _session = await _gateway.ConnectAsync(_settings, stoppingToken).ConfigureAwait(false);

            _sessionId = _session.SessionId;
            LogConnected(ProcessorId, WorkerId, _session.SessionId.Value);
            ProcessorDiagnostics.WorkerConnected(ProcessorId, WorkerId, _session.SessionId.Value);

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
        _progress.Dispose();

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

        _progress.Reset(positions, startFrom);

        LogStateRestored(ProcessorId, WorkerId, startFrom, positions.Count);

        await _session!
            .SendAsync(new WorkerReady(startFrom, positions))
            .ConfigureAwait(false);

        Status = ProcessorWorkerStatus.Processing;

        LogProcessing(ProcessorId, WorkerId);
    }

    async ValueTask HandleRecordBatch(RecordBatch batch, CancellationToken ct) {
        var scope = ProcessorDiagnostics.TrackBatch(ProcessorId, WorkerId);

        await _processor
            .ProcessRecords(new ProcessRecordsContext(ProcessorId, WorkerId, _sessionId, batch, Progress, ct))
            .ConfigureAwait(false);

        _progress.Track(batch);

        scope.Complete(batch.Count, Progress.Throughput.TotalRecords);

        LogBatchProcessed(ProcessorId, WorkerId, batch.Count, Progress.Throughput.TotalRecords);
    }

    async ValueTask HandleCheckpointBarrier(CheckpointBarrier barrier, CancellationToken ct) {
        var scope = ProcessorDiagnostics.TrackCheckpoint(ProcessorId, WorkerId);

        LogCheckpointBarrierReceived(ProcessorId, WorkerId, barrier.Trigger, barrier.BarrierId);

        var pending = _progress.PrepareCheckpoint(barrier);

        await _checkpointStore
            .StoreAsync(WorkerId, barrier.BarrierId, pending.Positions, ct)
            .ConfigureAwait(false);

        await _session!
            .SendAsync(new CheckpointComplete(barrier.BarrierId, pending.Positions))
            .ConfigureAwait(false);

        pending.Complete();

        scope.Complete(barrier.BarrierId, pending.Positions.Count);

        LogCheckpointCompleted(ProcessorId, WorkerId, barrier.BarrierId, pending.Positions.Count);

        await _processor
            .OnCheckpointComplete(new CheckpointCompleteContext(ProcessorId, WorkerId, _sessionId, Progress, ct))
            .ConfigureAwait(false);
    }

    async ValueTask HandleShutdown(Exception? ex = null) {
        Status = ex is null ? ProcessorWorkerStatus.Disconnected : ProcessorWorkerStatus.Failed;

        // capture final snapshot
        var progress = Progress;

        ProcessorDiagnostics.WorkerDisconnected(
            ProcessorId,
            WorkerId,
            progress.Throughput.TotalRecords,
            progress.Throughput.Elapsed,
            ex is not null
        );

        if (ex is null)
            LogShutdownCompleted(ProcessorId, WorkerId, progress.Throughput.TotalRecords, progress.Throughput.Elapsed);
        else
            LogShutdownFailed(ex, ProcessorId, WorkerId);

        await _processor
            .OnShutdown(ShutdownContext.From(ProcessorId, WorkerId, _sessionId, progress, ex))
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
    partial void LogCheckpointBarrierReceived(string processorId, string workerId, CheckpointBarrierTrigger trigger, LogPosition barrierId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} checkpoint completed (barrierId: {BarrierId}, partitions: {PartitionCount})")]
    partial void LogCheckpointCompleted(string processorId, string workerId, LogPosition barrierId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processor {ProcessorId} worker {WorkerId} shutdown completed ({TotalRecords} records in {Duration})")]
    partial void LogShutdownCompleted(string processorId, string workerId, long totalRecords, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processor {ProcessorId} worker {WorkerId} shutdown with error")]
    partial void LogShutdownFailed(Exception ex, string processorId, string workerId);

    #endregion
}
