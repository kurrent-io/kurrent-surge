using System.Diagnostics;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming.Diagnostics;

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

    public void RecordProcessed(long totalRecords, double? durationSeconds, long uncommittedRecords, long uncommittedBytes) {
        LogRecordProcessed(_processorId, _droneId, totalRecords);
        Publish(new RecordProcessedEvent(_processorId, _droneId, totalRecords, durationSeconds, uncommittedRecords, uncommittedBytes));
    }

    public void BatchProcessed(long totalRecords, int processedCount, double? durationSeconds, long uncommittedRecords, long uncommittedBytes) {
        LogBatchProcessed(_processorId, _droneId, totalRecords, processedCount);
        Publish(new BatchProcessedEvent(_processorId, _droneId, totalRecords, processedCount, durationSeconds, uncommittedRecords, uncommittedBytes));
    }

    /// <summary>
    /// One shutdown warning emitted when AutoTrack is off, records were dispatched, and nothing was ever
    /// committed — the subscription replays in full on restart, so the silent no-progress is surfaced.
    /// </summary>
    public void TrackingDisabledNoProgress() =>
        LogTrackingDisabledNoProgress(_processorId, _droneId);

    public void SourceLagSample(long lagBytes, double lagSeconds) =>
        Publish(new SourceLagSampleEvent(_processorId, _droneId, lagBytes, lagSeconds));

    public void CheckpointInitiated(string checkpointId, CheckpointTrigger trigger) {
        LogCheckpointInitiated(_processorId, _droneId, checkpointId, trigger);
        Publish(new CheckpointInitiatedEvent(_processorId, _droneId, trigger.ToString(), checkpointId));
    }

    public void CheckpointComplete(string checkpointId, CheckpointTrigger trigger, RecordPosition[] positions, double durationSeconds) {
        LogCheckpointComplete(_processorId, _droneId, checkpointId, trigger, positions);
        Publish(new CheckpointCompletedEvent(_processorId, _droneId, trigger.ToString(), checkpointId, positions.Length, durationSeconds));
    }

    public void CheckpointFailed(string checkpointId, CheckpointTrigger trigger, RecordPosition[] positions, Exception ex) {
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
    /// Opens a tracing span and a duration timer around the real <c>ProcessBatch</c> call. The batch — not
    /// the record — is the unit of work on this path; call <see cref="BatchScope.Complete"/> once after the
    /// frontier is resolved. The timer arms only when an observer is listening, so it costs nothing otherwise.
    /// </summary>
    /// <param name="batchSize">Number of records dispatched to <c>ProcessBatch</c> (tagged on the span).</param>
    public BatchScope TrackBatch(int batchSize) {
        var activity = ActivitySource.StartActivity(
            "processor.process_batch",
            ActivityKind.Consumer,
            parentContext: default,
            tags: [
                new("processor.id", _processorId),
                new("processor.drone_id", _droneId),
                new("batch.size", batchSize)
            ]
        );

        var timestamp = IsEnabled("BatchProcessed") ? Stopwatch.GetTimestamp() : 0;
        return new BatchScope(this, timestamp, activity);
    }

    /// <summary>
    /// Times one <c>ProcessBatch</c> invocation and its tracing span. Call <see cref="Complete"/> after the
    /// committed frontier is resolved. If <c>ProcessBatch</c> throws, the scope is disposed without
    /// completion and the span is marked as an error — no <c>BatchProcessed</c> event is published.
    /// </summary>
    public struct BatchScope : IDisposable {
        readonly DroneTelemetry? _diagnostics;
        readonly long            _startTimestamp;
        readonly Activity?       _activity;

        bool _completed;

        internal BatchScope(DroneTelemetry? diagnostics, long startTimestamp, Activity? activity) {
            _diagnostics    = diagnostics;
            _startTimestamp = startTimestamp;
            _activity       = activity;
            _completed      = false;
        }

        /// <summary>
        /// Marks the batch processed with success status.
        /// </summary>
        /// <param name="processedCount">Effective batch size — records at or below the committed frontier.</param>
        /// <param name="totalRecords">Total records processed so far (throughput).</param>
        /// <param name="uncommittedRecords">Records tracked but not yet committed (local replay window).</param>
        /// <param name="uncommittedBytes">Byte span tracked but not yet committed (local replay window).</param>
        public void Complete(int processedCount, long totalRecords, long uncommittedRecords, long uncommittedBytes) {
            if (_completed) return;

            _completed = true;

            // Measured from Stopwatch ticks, emitted in seconds (base unit) with sub-ms precision retained.
            double? durationSeconds = _startTimestamp != 0
                ? Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds
                : null;

            _diagnostics?.BatchProcessed(totalRecords, processedCount, durationSeconds, uncommittedRecords, uncommittedBytes);

            if (_activity is not null) {
                _activity.SetTag("processed.count", processedCount);
                _activity.SetTag("record.total_records", totalRecords);
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
        /// Disposes the scope. If not completed (e.g. <c>ProcessBatch</c> threw), marks as error.
        /// </summary>
        public void Dispose() {
            if (_completed) return;

            _completed = true;

            _activity?.SetStatus(ActivityStatusCode.Error, "Scope disposed without completion");
            _activity?.Dispose();
        }
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
        /// <param name="uncommittedRecords">Records tracked but not yet committed (local replay window).</param>
        /// <param name="uncommittedBytes">Byte span tracked but not yet committed (local replay window).</param>
        public void Complete(long totalRecords, long uncommittedRecords, long uncommittedBytes) {
            if (_completed) return;

            _completed = true;

            // Measured from Stopwatch ticks, emitted in seconds (base unit) with sub-ms precision retained.
            double? durationSeconds = _startTimestamp != 0
                ? Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds
                : null;

            _diagnostics?.RecordProcessed(totalRecords, durationSeconds, uncommittedRecords, uncommittedBytes);

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Starting")]
    partial void LogStarting(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Restoring state ({PartitionCount} partitions)")]
    partial void LogRestoringState(string processorId, string droneId, int partitionCount);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{DroneId}] State restored (position: {StartPosition}, partitions: {PartitionCount})")]
    partial void LogStateRestored(string processorId, string droneId, LogPosition startPosition, int partitionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DroneId}] Processing (sessionId: {SessionId})")]
    partial void LogProcessing(string processorId, string droneId, Guid sessionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Record processed (total: {TotalRecords})")]
    partial void LogRecordProcessed(string processorId, string droneId, long totalRecords);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Batch processed (committed: {ProcessedCount}, total: {TotalRecords})")]
    partial void LogBatchProcessed(string processorId, string droneId, long totalRecords, int processedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{DroneId}] AutoTrack disabled and no position was ever tracked — no progress persisted; the subscription replays fully on restart.")]
    partial void LogTrackingDisabledNoProgress(string processorId, string droneId);

    [LoggerMessage(Level = LogLevel.Trace, Message = "[{DroneId}] Checkpoint {CheckpointId} ({Trigger}) initiated")]
    partial void LogCheckpointInitiated(string processorId, string droneId, string checkpointId, CheckpointTrigger trigger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Checkpoint {CheckpointId} ({Trigger}) completed (positions: [{Positions}])")]
    partial void LogCheckpointComplete(string processorId, string droneId, string checkpointId, CheckpointTrigger trigger, RecordPosition[] positions);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DroneId}] Checkpoint {CheckpointId} ({Trigger}) failed: {Message}")]
    partial void LogCheckpointFailed(Exception ex, string processorId, string droneId, string checkpointId, CheckpointTrigger trigger, RecordPosition[] positions, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DroneId}] Stopping ({TotalRecords} records in {Duration} | {RecordsPerSecond:F1} rec/s)")]
    partial void LogStopping(string processorId, string droneId, long totalRecords, TimeSpan duration, double recordsPerSecond);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DroneId}] Stopped Gracefully {Reason}")]
    partial void LogStopped(string processorId, string droneId, string? reason = null);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DroneId}] Error: {Message}")]
    partial void LogError(Exception ex, string processorId, string droneId, string message);

    #endregion
}
