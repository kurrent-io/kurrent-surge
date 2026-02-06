using System.Diagnostics;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;

namespace Kurrent.Processors.Diagnostics;

sealed partial class WorkerTelemetry : TelemetrySource {
    readonly string _processorId;
    readonly string _workerId;

    internal WorkerTelemetry(ILogger logger, string processorId, string workerId)
        : base(TelemetryConstants.Listener, TelemetryConstants.Activities, logger) {
        _processorId = processorId;
        _workerId    = workerId;
    }

    public void Starting() {
        LogStarting(_processorId, _workerId);
        Publish("WorkerStarting", new WorkerStartingEvent(_processorId, _workerId));
    }

    public void RestoringState(int partitionCount) {
        LogRestoringState(_processorId, _workerId, partitionCount);
        Publish("WorkerRestoringState", new WorkerRestoringStateEvent(_processorId, _workerId, partitionCount));
    }

    public void StateRestored(LogPosition startPosition, int partitionCount) {
        LogStateRestored(_processorId, _workerId, startPosition, partitionCount);
        Publish("WorkerStateRestored", new WorkerStateRestoredEvent(_processorId, _workerId, startPosition, partitionCount));
    }

    public void Processing(Guid sessionId) {
        LogProcessing(_processorId, _workerId, sessionId);
        Publish("WorkerProcessing", new WorkerProcessingEvent(_processorId, _workerId, sessionId));
    }

    public void BatchProcessed(int recordCount, long totalRecords) =>
        BatchProcessedWithTiming(recordCount, totalRecords, 0);

    internal void BatchProcessedWithTiming(int recordCount, long totalRecords, double elapsedMs) {
        LogBatchProcessed(_processorId, _workerId, recordCount, totalRecords);
        Publish("BatchProcessed", new BatchProcessedEvent(_processorId, _workerId, recordCount, totalRecords, elapsedMs));
    }

    public void Checkpointing(CheckpointTrigger trigger, string checkpointId) {
        LogCheckpointing(_processorId, _workerId, trigger, checkpointId);
        Publish("WorkerCheckpointing", new WorkerCheckpointingEvent(_processorId, _workerId, trigger.ToString(), checkpointId));
    }

    public void CheckpointComplete(CheckpointTrigger trigger, string checkpointId, PendingPositionsFlush positions) {
        LogCheckpointComplete(_processorId, _workerId, trigger, checkpointId, positions);
        Publish("CheckpointCompleted", new CheckpointCompletedEvent(
            _processorId, _workerId, trigger.ToString(), checkpointId, positions.Positions.Count, 0));
    }

    public void Stopping(long totalRecords, TimeSpan duration, double recordsPerSecond) {
        LogStopping(_processorId, _workerId, totalRecords, duration, recordsPerSecond);
        Publish("WorkerStopping", new WorkerStoppingEvent(_processorId, _workerId, totalRecords, duration, recordsPerSecond));
    }

    public void Stopped(long totalRecords, TimeSpan duration) {
        LogStopped(_processorId, _workerId);
        Publish("WorkerStopped", new WorkerStoppedEvent(_processorId, _workerId, totalRecords, duration));
    }

    public void FinalCheckpoint(int partitionCount) {
        LogFinalCheckpoint(_processorId, _workerId, partitionCount);
        Publish("WorkerFinalCheckpoint", new WorkerFinalCheckpointEvent(_processorId, _workerId, partitionCount));
    }

    public void FinalCheckpointFailed(Exception ex) {
        LogFinalCheckpointFailed(ex, _processorId, _workerId, ex.Message);
        Publish("WorkerFinalCheckpointFailed", new WorkerFinalCheckpointFailedEvent(
            _processorId, _workerId, ex.GetType().FullName ?? ex.GetType().Name, ex.Message));
    }

    public void Error(Exception ex) {
        LogError(ex, _processorId, _workerId, ex.Message);
        Publish("WorkerError", new WorkerErrorEvent(
            _processorId, _workerId, ex.GetType().FullName ?? ex.GetType().Name, ex.Message));
    }

    public BatchScope TrackBatch() {
        var activity = ActivitySource.StartActivity(
            "processor.process_batch",
            ActivityKind.Consumer,
            parentContext: default,
            tags: [
                new("processor.id", _processorId),
                new("processor.worker_id", _workerId)
            ]
        );

        var timestamp = IsEnabled("BatchProcessed") ? Stopwatch.GetTimestamp() : 0;
        return new BatchScope(this, timestamp, activity);
    }

    /// <summary>
    /// Tracks batch processing duration and tracing span. Call <see cref="Complete"/> when done.
    /// Manages both metrics (via DiagnosticSource) and tracing (via Activity).
    /// </summary>
    public struct BatchScope : IDisposable {
        readonly WorkerTelemetry? _diagnostics;
        readonly long               _startTimestamp;
        readonly Activity?          _activity;

        bool _completed;

        internal BatchScope(WorkerTelemetry? diagnostics, long startTimestamp, Activity? activity) {
            _diagnostics    = diagnostics;
            _startTimestamp = startTimestamp;
            _activity       = activity;
            _completed      = false;
        }

        /// <summary>
        /// Marks the batch as complete with success status.
        /// </summary>
        /// <param name="batchSize">Number of records in the batch.</param>
        /// <param name="totalRecords">Total records processed so far.</param>
        public void Complete(int batchSize, long totalRecords) {
            if (_completed) return;

            _completed = true;

            // Calculate elapsed time and delegate to WorkerDiagnostics
            var elapsedMs = _startTimestamp != 0
                ? Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds
                : 0;

            _diagnostics?.BatchProcessedWithTiming(batchSize, totalRecords, elapsedMs);

            // Complete the tracing span
            if (_activity is not null) {
                _activity.SetTag("batch.size", batchSize);
                _activity.SetTag("batch.total_records", totalRecords);
                _activity.SetStatus(ActivityStatusCode.Ok);
                _activity.Dispose();
            }
        }

        /// <summary>
        /// Marks the batch as failed with error status.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        public void Fail(Exception exception) {
            if (_completed) return;

            _completed = true;

            if (_activity is null) return;

            _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity.AddEvent(
                new ActivityEvent(
                    "exception", tags: new ActivityTagsCollection {
                        { "exception.type", exception.GetType().FullName },
                        { "exception.message", exception.Message }
                    }
                )
            );

            _activity.Dispose();
        }

        /// <summary>
        /// Disposes the scope. If not completed, marks as error.
        /// </summary>
        public void Dispose() {
            if (_completed) return;

            _completed = true;

            // If disposed without Complete/Fail, treat as abandoned
            _activity?.SetStatus(ActivityStatusCode.Error, "Scope disposed without completion");
            _activity?.Dispose();
        }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Starting")]
    partial void LogStarting(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Restoring state ({PartitionCount} partitions)")]
    partial void LogRestoringState(string processorId, string workerId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{WorkerId}] State restored (position: {StartPosition}, partitions: {PartitionCount})")]
    partial void LogStateRestored(string processorId, string workerId, LogPosition startPosition, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{WorkerId}] Processing (sessionId: {SessionId})")]
    partial void LogProcessing(string processorId, string workerId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Batch processed ({RecordCount} records, total: {TotalRecords})")]
    partial void LogBatchProcessed(string processorId, string workerId, int recordCount, long totalRecords);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{WorkerId}] Checkpointing (trigger: {Trigger}, checkpointId: {CheckpointId})")]
    partial void LogCheckpointing(string processorId, string workerId, CheckpointTrigger trigger, string checkpointId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Checkpoint completed (trigger: {Trigger}, checkpointId: {CheckpointId}, positions: [{Positions}])")]
    partial void LogCheckpointComplete(string processorId, string workerId, CheckpointTrigger trigger, string checkpointId, PendingPositionsFlush positions);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{WorkerId}] Stopping ({TotalRecords} records in {Duration} | {RecordsPerSecond:F1} rec/s)")]
    partial void LogStopping(string processorId, string workerId, long totalRecords, TimeSpan duration, double recordsPerSecond);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{WorkerId}] Stopped")]
    partial void LogStopped(string processorId, string workerId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{WorkerId}] Final checkpoint ({PartitionCount} partitions)")]
    partial void LogFinalCheckpoint(string processorId, string workerId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{ProcessorId}/{WorkerId}] Final checkpoint failed: {Message}")]
    partial void LogFinalCheckpointFailed(Exception ex, string processorId, string workerId, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{ProcessorId}/{WorkerId}] Error: {Message}")]
    partial void LogError(Exception ex, string processorId, string workerId, string message);

    #endregion
}
