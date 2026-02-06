// IMPORTAN! DO NOT DELETE THIS FILE — it contains the full implementation of ProcessorWorker with detailed logging, which is currently being refactored. It will be used as reference and potentially merged back after refactor completion.

// using System.Collections.Immutable;
// using Kurrent.Client;
// using Kurrent.Client.Processors.Gateway.Messages;
// using Kurrent.Client.Streams;
// using Kurrent.Processors.Checkpointing;
// using Kurrent.Processors.Gateway;
// using Kurrent.Processors.Models;
// using Kurrent.Processors.Options;
// using CheckpointStore = Kurrent.Processors.Checkpointing.CheckpointStore;
//
// namespace Kurrent.Processors;
//
// sealed partial class ProcessorWorker : IProcessorWorker {
//     readonly ProcessorSettings _settings;
//     readonly KurrentClient     _client;
//     readonly IProcessorGateway _gateway;
//     readonly ILogger           _logger;
//     readonly CheckpointStore   _checkpointStore;
//     readonly PartitionedPositionTracker _tracker;
//     readonly IRecordProcessor _processor;
//     readonly bool             _ownsClient;
//     readonly ProgressTracker  _progress;
//
//     int _disposed;
//     int _started;
//
//     WorkerContext WorkerContext = WorkerContext.None;
//
//     public ProcessorWorker(
//         ProcessorSettings settings,
//         KurrentClient client,
//         IRecordProcessor processor,
//         ILoggerFactory loggerFactory,
//         TimeProvider? time = null,
//         bool ownsClient = true,
//         IProcessorGateway? gateway = null
//     ) {
//         _client     = client;
//         _processor  = new SafeRecordProcessor(processor);
//         _logger     = loggerFactory.CreateLogger(processor.GetType().Name);
//         _ownsClient = ownsClient && !client.Shared;
//
//         _settings = settings;
//
//         time ??= TimeProvider.System;
//
//         _gateway = gateway ?? new LegacyGateway(_client);
//
//         _checkpointStore = new CheckpointStore(
//             _settings.ProcessorId,
//             _settings.MaxParallelism,
//             _client.Streams,
//             time,
//             loggerFactory.CreateLogger<CheckpointStore>());
//
//         _progress = new ProgressTracker(time);
//         _tracker  = new PartitionedPositionTracker();
//     }
//
//     public string            ProcessorId => _settings.ProcessorId;
//     public string            WorkerId    => _settings.WorkerId;
//     public ProcessorSettings Settings    => _settings;
//
//     /// <summary>
//     /// Current lifecycle status of the processor worker.
//     /// </summary>
//     public ProcessorWorkerStatus Status { get; private set; } = ProcessorWorkerStatus.Starting;
//
//     /// <summary>
//     /// Current progress snapshot.
//     /// </summary>
//     public WorkerProgress Progress => _progress.Progress;
//
//     public async Task RunAsync(CancellationToken stoppingToken) {
//         if (Interlocked.Exchange(ref _started, 1) == 1)
//             throw new InvalidOperationException("This worker has already been started at least once. Create a new instance to run again.");
//
//         Status = ProcessorWorkerStatus.Starting;
//         LogStarting(ProcessorId, WorkerId);
//
//         WorkerContext = new() {
//             ProcessorId       = ProcessorId,
//             WorkerId          = WorkerId,
//             SessionId         = SessionId.None,
//             Logger            = _logger,
//             CancellationToken = stoppingToken
//         };
//
//         IWorkerSession session = null!;
//
//         // used to cancel pump/timer before shutdown logic
//         using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
//
//         try {
//             session = await _gateway
//                 .ConnectAsync(_settings, cancellator.Token)
//                 .ConfigureAwait(false);
//
//             WorkerContext = WorkerContext with {
//                 SessionId = session.SessionId
//             };
//
//             await foreach (var message in session.WithCancellation(stoppingToken).ConfigureAwait(false)) {
//                 await message
//                     .SwitchAsync(
//                         msg => HandleWorkOrder(msg, session, stoppingToken),
//                         msg => HandleRecordBatch(msg, session, stoppingToken),
//                         msg => HandleCheckpoint(msg, session, stoppingToken)
//                     )
//                     .ConfigureAwait(false);
//             }
//
//             await HandleStopped(session, cancellator).ConfigureAwait(false);
//         }
//         catch (OperationCanceledException) {
//             await HandleStopped(session, cancellator).ConfigureAwait(false);
//         }
//         catch (Exception ex) {
//             await HandleError(session, cancellator, ex).ConfigureAwait(false);
//         }
//         finally {
//             await Cleanup(session).ConfigureAwait(false);
//         }
//     }
//
//     async ValueTask HandleWorkOrder(WorkOrder workOrder, IWorkerSession session, CancellationToken ct) {
//         LogRestoringState(ProcessorId, WorkerId, workOrder.Partitions.Length);
//
//         // Load checkpoint with resolved positions for all assigned partitions
//         var checkpoint = await _checkpointStore
//             .LoadLatestAsync(
//                 workOrder.Partitions.ToArray(),
//                 _settings.Subscription.InitialPosition,
//                 ct)
//             .ConfigureAwait(false);
//
//         // Extract positions for tracker and WorkerReady
//         var positions = checkpoint.Partitions
//             .ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value.Position);
//
//         _tracker.Reset(positions);
//         _progress.SetPartitionTracker(_tracker);
//
//         LogStateRestored(ProcessorId, WorkerId, checkpoint.LowWatermark.LogPosition, positions.Count);
//
//         await session!
//             .SendAsync(new WorkerReady(checkpoint.CheckpointId, PartitionPositions.From(positions)))
//             .ConfigureAwait(false);
//
//         Status = ProcessorWorkerStatus.Processing;
//
//         LogProcessing(ProcessorId, WorkerId, session.SessionId.Value);
//     }
//
//     async ValueTask HandleRecordBatch(RecordBatch batch, IWorkerSession session, CancellationToken ct) {
//         await _processor
//             .ProcessRecords(WorkerContext.ForProcessRecords(batch, Progress))
//             .ConfigureAwait(false);
//
//         _tracker.Track(batch);
//         _progress.TrackRecords(batch.Count);
//
//         LogBatchProcessed(
//             ProcessorId, WorkerId, batch.Count,
//             Progress.Throughput.TotalRecords
//         );
//     }
//
//     async ValueTask HandleCheckpoint(Checkpoint checkpoint, IWorkerSession session, CancellationToken ct) {
//         LogCheckpointing(ProcessorId, WorkerId, checkpoint.Trigger, checkpoint.CheckpointId);
//
//         var pendingFlush = _tracker.PrepareFlush();
//
//         await _processor
//             .OnCheckpoint(WorkerContext.ForCheckpoint(checkpoint))
//             .ConfigureAwait(false);
//
//         await _checkpointStore
//             .StoreAsync(WorkerId, checkpoint.CheckpointId, pendingFlush.Positions, ct)
//             .ConfigureAwait(false);
//
//         await session
//             .SendAsync(new CheckpointComplete(checkpoint.CheckpointId, PartitionPositions.From(pendingFlush.Positions)))
//             .ConfigureAwait(false);
//
//         pendingFlush.Execute();
//         _progress.TrackCheckpoint(checkpoint);
//
//         LogCheckpointComplete(ProcessorId, WorkerId, checkpoint.Trigger, checkpoint.CheckpointId, pendingFlush);
//     }
//
//     async ValueTask HandleStopped(IWorkerSession session, CancellationTokenSource cts) {
//         // Kill pump and timer immediately (idempotent if already cancelled by stoppingToken)
//         await cts.CancelAsync().ConfigureAwait(false);
//
//         var progress = Progress;
//         var throughput = progress.Throughput;
//
//         LogStopping(
//             ProcessorId, WorkerId,
//             throughput.TotalRecords, throughput.Elapsed,
//             throughput.RecordsPerSecond
//         );
//
//         // Final checkpoint flush (best-effort)
//         // Only attempt if we reached Processing state (partitions assigned, tracker initialized)
//         if (Status == ProcessorWorkerStatus.Processing) {
//             try {
//                 var pendingFlush = _tracker.PrepareFlush();
//                 var checkpointId = CheckpointId.New();
//
//                 LogFinalCheckpoint(ProcessorId, WorkerId, pendingFlush.Positions.Count);
//
//                 var checkpoint = new Checkpoint(
//                     checkpointId,
//                     CheckpointTrigger.Shutdown,
//                     DateTimeOffset.UtcNow
//                 );
//
//                 // CancellationToken.None — cts is cancelled, stoppingToken may be cancelled
//                 await _processor
//                     .OnCheckpoint(
//                         (WorkerContext with { CancellationToken = CancellationToken.None })
//                             .ForCheckpoint(checkpoint))
//                     .ConfigureAwait(false);
//
//                 await _checkpointStore
//                     .StoreAsync(WorkerId, checkpointId, pendingFlush.Positions, CancellationToken.None)
//                     .ConfigureAwait(false);
//
//                 pendingFlush.Execute();
//                 _progress.TrackCheckpoint(checkpoint);
//             }
//             catch (Exception ex) {
//                 // Best-effort: positions replay on restart (at-least-once guarantee preserved)
//                 LogFinalCheckpointFailed(ex, ProcessorId, WorkerId, ex.Message);
//             }
//         }
//
//         // Send Disconnect (best-effort, no-op in legacy)
//         try {
//             await session
//                 .SendAsync(new Disconnect(DisconnectReason.Stopped, PartitionPositions.Empty))
//                 .ConfigureAwait(false);
//         }
//         catch {
//             // Swallow — protocol signaling, not critical
//         }
//
//         // OnComplete callback (throws on failure, preserving current behavior)
//         try {
//             await _processor
//                 .OnComplete(WorkerContext.ForComplete(Progress))
//                 .ConfigureAwait(false);
//
//             Status = ProcessorWorkerStatus.Stopped;
//             LogStopped(ProcessorId, WorkerId);
//         }
//         catch (Exception completeEx) {
//             Status = ProcessorWorkerStatus.Error;
//             LogError(completeEx, ProcessorId, WorkerId, completeEx.Message);
//             throw;
//         }
//     }
//
//     async ValueTask HandleError(IWorkerSession session, CancellationTokenSource cts, Exception ex) {
//         // Kill pump and timer immediately
//         await cts.CancelAsync().ConfigureAwait(false);
//
//         var progress = Progress;
//         var throughput = progress.Throughput;
//
//         LogStopping(
//             ProcessorId, WorkerId,
//             throughput.TotalRecords, throughput.Elapsed,
//             throughput.RecordsPerSecond
//         );
//
//         // OnError callback — aggregate on failure
//         try {
//             await _processor
//                 .OnError(WorkerContext.ForError(progress, ex))
//                 .ConfigureAwait(false);
//         }
//         catch (Exception uex) {
//             ex = new AggregateException(ex, uex).Flatten();
//         }
//
//         // Send Disconnect (best-effort, no-op in legacy)
//         try {
//             await session
//                 .SendAsync(new Disconnect(DisconnectReason.Error, PartitionPositions.Empty))
//                 .ConfigureAwait(false);
//         }
//         catch {
//             // Swallow — protocol signaling, not critical
//         }
//
//         Status = ProcessorWorkerStatus.Error;
//         LogError(ex, ProcessorId, WorkerId, ex.Message);
//         throw ex;
//     }
//
//     async ValueTask Cleanup(IWorkerSession? session) {
//         if (Interlocked.Exchange(ref _disposed, 1) == 1)
//             return;
//
//         List<Exception>? errors = null;
//
//         try {
//             if (session is not null)
//                 await session.DisposeAsync().ConfigureAwait(false);
//         }
//         catch (Exception ex) {
//             LogError(ex, ProcessorId, WorkerId, ex.Message);
//             (errors ??= []).Add(ex);
//         }
//
//         try {
//             _tracker.Dispose();
//         }
//         catch (Exception ex) {
//             LogError(ex, ProcessorId, WorkerId, ex.Message);
//             (errors ??= []).Add(ex);
//         }
//
//         try {
//             if (_ownsClient)
//                 await _client.DisposeAsync().ConfigureAwait(false);
//         }
//         catch (Exception ex) {
//             LogError(ex, ProcessorId, WorkerId, ex.Message);
//             (errors ??= []).Add(ex);
//         }
//
//         if (errors is { Count: > 0 })
//             throw new AggregateException("One or more errors during processor cleanup", errors);
//     }
//
//     #region Logging
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Starting")]
//     partial void LogStarting(string processorId, string workerId);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Restoring state ({PartitionCount} partitions)")]
//     partial void LogRestoringState(string processorId, string workerId, int partitionCount);
//
//     [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{WorkerId}] State restored (position: {StartPosition}, partitions: {PartitionCount})")]
//     partial void LogStateRestored(string processorId, string workerId, LogPosition startPosition, int partitionCount);
//
//     [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{WorkerId}] Processing (sessionId: {SessionId})")]
//     partial void LogProcessing(string processorId, string workerId, Guid sessionId);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Batch processed ({RecordCount} records, total: {TotalRecords})")]
//     partial void LogBatchProcessed(string processorId, string workerId, int recordCount, long totalRecords);
//
//     [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{WorkerId}] Checkpointing (trigger: {Trigger}, checkpointId: {CheckpointId})")]
//     partial void LogCheckpointing(string processorId, string workerId, CheckpointTrigger trigger, string checkpointId);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Checkpoint completed (trigger: {Trigger}, checkpointId: {CheckpointId}, positions: [{Positions}])")]
//     partial void LogCheckpointComplete(string processorId, string workerId, CheckpointTrigger trigger, string checkpointId, PendingPositionsFlush positions);
//
//     [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{WorkerId}] Stopping ({TotalRecords} records in {Duration} | {RecordsPerSecond:F1} rec/s)")]
//     partial void LogStopping(string processorId, string workerId, long totalRecords, TimeSpan duration, double recordsPerSecond);
//
//     [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{WorkerId}] Stopped")]
//     partial void LogStopped(string processorId, string workerId);
//
//     [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Final checkpoint ({PartitionCount} partitions)")]
//     partial void LogFinalCheckpoint(string processorId, string workerId, int partitionCount);
//
//     [LoggerMessage(Level = LogLevel.Warning, Message = "[{ProcessorId}/{WorkerId}] Final checkpoint failed: {Message}")]
//     partial void LogFinalCheckpointFailed(Exception ex, string processorId, string workerId, string message);
//
//     [LoggerMessage(Level = LogLevel.Error, Message = "[{ProcessorId}/{WorkerId}] Error: {Message}")]
//     partial void LogError(Exception ex, string processorId, string workerId, string message);
//
//     #endregion
// }
//
// /// <summary>
// /// Represents a running processor worker instance.
// /// </summary>
// [PublicAPI]
// public interface IProcessorWorker {
//     /// <summary>
//     /// Unique identifier of the processor being executed.
//     /// </summary>
//     string ProcessorId { get; }
//
//     /// <summary>
//     /// The unique identifier of this worker instance.
//     /// Could have been automatically generated if not provided in settings.
//     /// </summary>
//     string WorkerId { get; }
//
//     /// <summary>
//     /// Current lifecycle status of the processor worker.
//     /// </summary>
//     ProcessorWorkerStatus Status { get; }
//
//     /// <summary>
//     /// The configuration settings used by this processor worker.
//     /// </summary>
//     ProcessorSettings Settings { get; }
//
//     /// <summary>
//     /// Runs the processor worker asynchronously until cancellation is requested.
//     /// </summary>
//     /// <param name="stoppingToken">Token to request cancellation of processing.</param>
//     Task RunAsync(CancellationToken stoppingToken);
// }
//
// /// <summary>
// /// Defines the lifecycle status of a distributed processor worker.
// /// </summary>
// [PublicAPI]
// public enum ProcessorWorkerStatus {
//     /// <summary>
//     /// Fallback value for serialization and protocol compatibility.
//     /// </summary>
//     Unspecified = 0,
//
//     /// <summary>
//     /// Worker is bootstrapping: connecting, restoring state, and preparing to process.
//     /// </summary>
//     Starting = 1,
//
//     /// <summary>
//     /// Actively processing records.
//     /// </summary>
//     Processing = 2,
//
//     /// <summary>
//     /// Terminal status for a clean shutdown or cancellation.
//     /// </summary>
//     Stopped = 3,
//
//     /// <summary>
//     /// Terminal status for an unrecoverable error.
//     /// </summary>
//     Error = 4
// }
