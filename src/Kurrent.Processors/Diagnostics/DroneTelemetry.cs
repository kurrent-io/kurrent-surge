using System.Diagnostics;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;

namespace Kurrent.Processors.Diagnostics;

sealed partial class DroneTelemetry : TelemetrySource {
    readonly string _processorId;
    readonly string _droneId;

    internal DroneTelemetry(ILogger logger, string processorId, string droneId)
        : base(TelemetryConstants.Listener, TelemetryConstants.Activities, logger) {
        _processorId = processorId;
        _droneId     = droneId;
    }

    public void Starting() {
        LogStarting(_processorId, _droneId);
        Publish(new DroneStartingEvent(_processorId, _droneId));
    }

    public void RestoringState(int partitionCount) {
        LogRestoringState(_processorId, _droneId, partitionCount);
        Publish(new DroneRestoringStateEvent(_processorId, _droneId, partitionCount));
    }

    public void StateRestored(LogPosition startPosition, int partitionCount) {
        LogStateRestored(_processorId, _droneId, startPosition, partitionCount);
        Publish(new DroneStateRestoredEvent(_processorId, _droneId, startPosition, partitionCount));
    }

    public void Processing(Guid sessionId) {
        LogProcessing(_processorId, _droneId, sessionId);
        Publish(new DroneProcessingEvent(_processorId, _droneId, sessionId));
    }

    public void RecordProcessed(long totalRecords, double? elapsedMs = null) {
        LogRecordProcessed(_processorId, _droneId, totalRecords);
        Publish(new RecordProcessedEvent(_processorId, _droneId, totalRecords));
    }

    public void CheckpointInitiated(string checkpointId, CheckpointTrigger trigger) {
        LogCheckpointInitiated(_processorId, _droneId, checkpointId, trigger);
        Publish(new CheckpointInitiatedEvent(_processorId, _droneId, trigger.ToString(), checkpointId));
    }

    public void CheckpointComplete(string checkpointId, CheckpointTrigger trigger, PartitionPositions positions) {
        LogCheckpointComplete(_processorId, _droneId, checkpointId, trigger, positions);
        Publish(new CheckpointCompletedEvent(_processorId, _droneId, trigger.ToString(), checkpointId, positions.Count, 0));
    }

    public void CheckpointFailed(string checkpointId, CheckpointTrigger trigger, PartitionPositions positions, Exception ex) {
        LogCheckpointFailed(ex, _processorId, _droneId, checkpointId, trigger, positions, ex.Message);
        Publish(new CheckpointFailedEvent(_processorId, _droneId, checkpointId, trigger.ToString(), ex.GetType().FullName ?? ex.GetType().Name, ex.Message));
    }

    public void Stopping(long totalRecords, TimeSpan duration, double recordsPerSecond) {
        LogStopping(_processorId, _droneId, totalRecords, duration, recordsPerSecond);
        Publish(new DroneStoppingEvent(_processorId, _droneId, totalRecords, duration, recordsPerSecond));
    }

    public void Stopped(long totalRecords, TimeSpan duration, string? reason = null) {
        LogStopped(_processorId, _droneId, reason is { } ? $": {reason}" : null);
        Publish(new DroneStoppedEvent(_processorId, _droneId, totalRecords, duration));
    }

    public void Error(Exception ex) {
        LogError(ex, _processorId, _droneId, ex.Message);
        Publish(new DroneErrorEvent(_processorId, _droneId, ex.GetType().FullName ?? ex.GetType().Name, ex.Message));
    }

    public RecordScope TrackRecord() {
        var activity = ActivitySource.StartActivity(
            "processor.process_record",
            ActivityKind.Consumer,
            parentContext: default,
            tags: [
                new("processor.id", _processorId),
                new("processor.drone_id", _droneId)
            ]
        );

        var timestamp = IsEnabled("RecordProcessed") ? Stopwatch.GetTimestamp() : 0;
        return new RecordScope(this, timestamp, activity);
    }

    /// <summary>
    /// Tracks record processing duration and tracing span. Call <see cref="RecordScope.Complete"/> when done.
    /// Manages both metrics (via DiagnosticSource) and tracing (via Activity).
    /// </summary>
    public struct RecordScope : IDisposable {
        readonly DroneTelemetry? _diagnostics;
        readonly long            _startTimestamp;
        readonly Activity?       _activity;

        bool _completed;

        internal RecordScope(DroneTelemetry? diagnostics, long startTimestamp, Activity? activity) {
            _diagnostics    = diagnostics;
            _startTimestamp = startTimestamp;
            _activity       = activity;
            _completed      = false;
        }

        /// <summary>
        /// Marks the record as processed with success status.
        /// </summary>
        /// <param name="totalRecords">Total records processed so far.</param>
        public void Complete(long totalRecords) {
            if (_completed) return;

            _completed = true;

            double? elapsedMs = _startTimestamp != 0
                ? Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds
                : null;

            _diagnostics?.RecordProcessed(totalRecords, elapsedMs);

            // Complete the tracing span
            if (_activity is not null) {
                _activity.SetTag("record.total_records", totalRecords);
                _activity.SetStatus(ActivityStatusCode.Ok);
                _activity.Dispose();
            }
        }

        /// <summary>
        /// Marks the record as failed with error status.
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

    #region ->> Logging <<-

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{DroneId}] Starting")]
    partial void LogStarting(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{DroneId}] Restoring state ({PartitionCount} partitions)")]
    partial void LogRestoringState(string processorId, string droneId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{DroneId}] State restored (position: {StartPosition}, partitions: {PartitionCount})")]
    partial void LogStateRestored(string processorId, string droneId, LogPosition startPosition, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{DroneId}] Processing (sessionId: {SessionId})")]
    partial void LogProcessing(string processorId, string droneId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{DroneId}] Record processed (total: {TotalRecords})")]
    partial void LogRecordProcessed(string processorId, string droneId, long totalRecords);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{ProcessorId}/{DroneId}] Checkpoint {CheckpointId} ({Trigger}) initiated")]
    partial void LogCheckpointInitiated(string processorId, string droneId, string checkpointId, CheckpointTrigger trigger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{ProcessorId}/{DroneId}] Checkpoint {CheckpointId} ({Trigger}) completed (positions: [{Positions}])")]
    partial void LogCheckpointComplete(string processorId, string droneId, string checkpointId, CheckpointTrigger trigger, PartitionPositions positions);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{ProcessorId}/{DroneId}] Checkpoint {CheckpointId} ({Trigger}) failed: {Message}")]
    partial void LogCheckpointFailed(Exception ex, string processorId, string droneId, string checkpointId, CheckpointTrigger trigger, PartitionPositions positions, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{DroneId}] Stopping ({TotalRecords} records in {Duration} | {RecordsPerSecond:F1} rec/s)")]
    partial void LogStopping(string processorId, string droneId, long totalRecords, TimeSpan duration, double recordsPerSecond);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{ProcessorId}/{DroneId}] Stopped Gracefully {Reason}")]
    partial void LogStopped(string processorId, string droneId, string? reason = null);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{ProcessorId}/{DroneId}] Error: {Message}")]
    partial void LogError(Exception ex, string processorId, string droneId, string message);

    #endregion
}
