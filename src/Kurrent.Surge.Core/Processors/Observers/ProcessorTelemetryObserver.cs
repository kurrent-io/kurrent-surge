using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kurrent.Diagnostics;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Diagnostics;
using Kurrent.Surge.Streaming.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Streaming;

/// <summary>
/// The SDK's composite telemetry module: logging, metrics, tracing, and diagnostic-source
/// publishing behind a single observer — one dispatch per event for all built-in concerns.
/// </summary>
/// <remarks>
/// Built on <see cref="TelemetrySource"/> (dual-dispatch chassis: <c>[LoggerMessage]</c> logging +
/// guarded <see cref="DiagnosticSource"/> publishing), with the metric instruments and W3C tracing
/// absorbed — the SDK's built-in telemetry occupies one seat on the dispatcher; user observers
/// compose beside it on the seam.
/// </remarks>
sealed partial class ProcessorTelemetryObserver : TelemetrySource, IProcessorObserver {
    public ProcessorTelemetryObserver(ILogger logger, IMeterFactory meterFactory)
        : base(TelemetryConstants.Listener, TelemetryConstants.Activities, logger) {
        var meter = meterFactory.Create("Kurrent.Surge.Client.Processors");

        _recordsProcessed     = ProcessorMetrics.CreateRecordsProcessed(meter);
        _recordDuration       = ProcessorMetrics.CreateRecordDuration(meter);
        _batchDuration        = ProcessorMetrics.CreateBatchDuration(meter);
        _batchSize            = ProcessorMetrics.CreateBatchSize(meter);
        _checkpointsCompleted = ProcessorMetrics.CreateCheckpointsCompleted(meter);
        _checkpointsFailed    = ProcessorMetrics.CreateCheckpointsFailed(meter);
        _checkpointDuration   = ProcessorMetrics.CreateCheckpointDuration(meter);
        _droneErrors          = ProcessorMetrics.CreateDroneErrors(meter);
        _uncommittedRecords   = ProcessorMetrics.CreateUncommittedRecords(meter);
        _uncommittedBytes     = ProcessorMetrics.CreateUncommittedBytes(meter);
    }

    readonly RecordsProcessedCounter     _recordsProcessed;
    readonly RecordDurationHistogram     _recordDuration;
    readonly BatchDurationHistogram      _batchDuration;
    readonly BatchSizeHistogram          _batchSize;
    readonly CheckpointsCompletedCounter _checkpointsCompleted;
    readonly CheckpointsFailedCounter    _checkpointsFailed;
    readonly CheckpointDurationHistogram _checkpointDuration;
    readonly DroneErrorsCounter          _droneErrors;
    readonly UncommittedRecordsGauge     _uncommittedRecords;
    readonly UncommittedBytesGauge       _uncommittedBytes;

    // Dispatch is synchronous on the processing thread and Ready(n) always precedes the matching
    // Completed(n) with no interleaving, so one slot per unit type replaces a correlation map.
    Activity? _recordActivity;
    Activity? _batchActivity;

    public void OnStateChanged(in ProcessorStateChanged evt) {
        switch (evt.ToStatus) {
            case DroneStatus.Starting:
                LogStarting(evt.Info.DroneId);
                break;

            case DroneStatus.Processing:
                LogProcessing(evt.Info.DroneId, evt.SessionId);
                break;

            case DroneStatus.Stopped:
                LogStopped(evt.Info.DroneId);
                break;

            case DroneStatus.Error:
                LogError(evt.Error, evt.Info.DroneId, evt.Error?.Message ?? "Unknown error");
                _droneErrors.Add(1, evt.Info.ProcessorId.Value);
                break;
        }

        Publish(evt);
    }

    public void OnRecordSkipped(in RecordSkipped evt) {
        LogRecordSkipped(evt.Info.DroneId);
        Publish(evt);
    }

    public void OnRecordReady(in RecordReady evt) {
        if (ActivitySource.HasListeners()) {
            var activity = ActivitySource.StartActivity(
                $"process {evt.Record.Stream}",
                ActivityKind.Consumer,
                parentContext: evt.Record.ExtractTraceContext() ?? default,
                tags: [
                    new("messaging.system", MessagingSystem),
                    new("messaging.operation.type", "process"),
                    new("messaging.operation.name", "process"),
                    new("messaging.destination.name", evt.Record.Stream.ToString()),
                    new("messaging.message.id", evt.Record.Id.ToString()),
                    new("processor.id", evt.Info.ProcessorId.Value),
                    new("processor.drone_id", evt.Info.DroneId)
                ],
                links: null,
                startTime: evt.Timestamp
            );

            if (activity is not null) {
                evt.Record.InjectTraceContext(activity);
                _recordActivity = activity;
            }
        }

        Publish(evt);
    }

    public void OnRecordCompleted(in RecordCompleted evt) {
        switch (evt.Outcome) {
            case RecordOutcome.Processed:
                LogRecordProcessed(evt.Info.DroneId, evt.Elapsed);

                _recordsProcessed.Add(1, evt.Info.ProcessorId.Value, evt.Info.DroneId);
                _recordDuration.Record(evt.Elapsed.TotalSeconds, evt.Info.ProcessorId.Value);
                _uncommittedRecords.Record(evt.Progress.UncommittedRecords, evt.Info.ProcessorId.Value, evt.Info.DroneId);
                _uncommittedBytes.Record(evt.Progress.UncommittedBytes, evt.Info.ProcessorId.Value, evt.Info.DroneId);
                break;

            case RecordOutcome.Failed:
                LogRecordFailed(evt.Error, evt.Info.DroneId, evt.Error?.Message ?? "Unknown error");
                break;

            case RecordOutcome.Cancelled:
                LogRecordCancelled(evt.Info.DroneId);
                break;
        }

        var activity = _recordActivity;
        _recordActivity = null;

        if (activity is not null) {
            activity.SetEndTime(activity.StartTimeUtc.Add(evt.Elapsed));
            FinishSpan(activity, evt.Outcome, evt.Error);
        }

        Publish(evt);
    }

    public void OnBatchReady(in BatchReady evt) {
        if (ActivitySource.HasListeners()) {
            var activity = ActivitySource.StartActivity(
                "process",
                ActivityKind.Consumer,
                parentContext: default,
                tags: [
                    new("messaging.system", MessagingSystem),
                    new("messaging.operation.type", "process"),
                    new("messaging.operation.name", "process"),
                    new("messaging.batch.message_count", evt.Records.Count),
                    new("processor.id", evt.Info.ProcessorId.Value),
                    new("processor.drone_id", evt.Info.DroneId)
                ],
                links: evt.Records.ExtractTraceLinks(),
                startTime: evt.Timestamp
            );

            if (activity is not null) {
                foreach (var record in evt.Records)
                    record.InjectTraceContext(activity);

                _batchActivity = activity;
            }
        }

        Publish(evt);
    }

    public void OnBatchCompleted(in BatchCompleted evt) {
        switch (evt.Outcome) {
            case RecordOutcome.Processed:
                LogBatchProcessed(evt.Info.DroneId, evt.ProcessedCount, evt.Records.Count, evt.Elapsed);

                // records_processed is the committed prefix, added ONCE per batch (never per dispatched
                // record, so replays are not double-counted).
                _recordsProcessed.Add(evt.ProcessedCount, evt.Info.ProcessorId.Value, evt.Info.DroneId);
                _batchDuration.Record(evt.Elapsed.TotalSeconds, evt.Info.ProcessorId.Value);

                // record_duration on the batch path is the per-record average — keeps per-record latency
                // dashboards uniform without knowing which dispatch shape produced the data.
                if (evt.ProcessedCount > 0)
                    _recordDuration.Record(evt.Elapsed.TotalSeconds / evt.ProcessedCount, evt.Info.ProcessorId.Value);

                _batchSize.Record(evt.ProcessedCount, evt.Info.ProcessorId.Value);
                _uncommittedRecords.Record(evt.Progress.UncommittedRecords, evt.Info.ProcessorId.Value, evt.Info.DroneId);
                _uncommittedBytes.Record(evt.Progress.UncommittedBytes, evt.Info.ProcessorId.Value, evt.Info.DroneId);
                break;

            case RecordOutcome.Failed:
                LogBatchFailed(evt.Error, evt.Info.DroneId, evt.Records.Count, evt.Error?.Message ?? "Unknown error");
                break;

            case RecordOutcome.Cancelled:
                LogBatchCancelled(evt.Info.DroneId, evt.Records.Count);
                break;
        }

        var activity = _batchActivity;
        _batchActivity = null;

        if (activity is not null) {
            activity.SetEndTime(activity.StartTimeUtc.Add(evt.Elapsed));
            FinishSpan(activity, evt.Outcome, evt.Error);
        }

        Publish(evt);
    }

    public void OnCheckpointCommitted(in CheckpointCommitted evt) {
        LogCheckpointCommitted(evt.Info.DroneId, evt.CheckpointId, evt.Trigger, evt.Positions.Count, evt.Duration);

        _checkpointsCompleted.Add(1, evt.Info.ProcessorId.Value, evt.Info.DroneId, evt.Trigger.ToString());
        _checkpointDuration.Record(evt.Duration.TotalSeconds, evt.Info.ProcessorId.Value);

        Publish(evt);
    }

    public void OnCheckpointFailed(in CheckpointFailed evt) {
        LogCheckpointFailed(evt.Error, evt.Info.DroneId, evt.CheckpointId, evt.Trigger, evt.Error.Message);

        _checkpointsFailed.Add(1, evt.Info.ProcessorId.Value, evt.Info.DroneId, evt.Trigger.ToString());

        Publish(evt);
    }

    const string MessagingSystem = "kurrentdb";

    static void FinishSpan(Activity activity, RecordOutcome outcome, Exception? error) {
        switch (outcome) {
            case RecordOutcome.Processed:
                activity.SetStatus(ActivityStatusCode.Ok);
                break;

            case RecordOutcome.Failed:
                activity.SetStatus(ActivityStatusCode.Error, error?.Message);

                if (error is not null) {
                    activity.SetTag("error.type", error.GetType().FullName);
                    activity.AddException(error);
                }

                break;

            case RecordOutcome.Cancelled:
                activity.SetStatus(ActivityStatusCode.Error, "Processing was cancelled");
                break;
        }

        activity.Dispose();
    }

    #region ->> Logging <<-

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Starting")]
    partial void LogStarting(string droneId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DroneId}] Processing (sessionId: {SessionId})")]
    partial void LogProcessing(string droneId, SessionId sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{DroneId}] Stopped Gracefully")]
    partial void LogStopped(string droneId);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DroneId}] Error: {Message}")]
    partial void LogError(Exception? ex, string droneId, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Record skipped")]
    partial void LogRecordSkipped(string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Record processed ({Elapsed})")]
    partial void LogRecordProcessed(string droneId, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DroneId}] Record failed: {Message}")]
    partial void LogRecordFailed(Exception? ex, string droneId, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Record processing cancelled")]
    partial void LogRecordCancelled(string droneId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Batch processed (committed: {ProcessedCount}, records: {RecordCount}, elapsed: {Elapsed})")]
    partial void LogBatchProcessed(string droneId, int processedCount, int recordCount, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DroneId}] Batch failed (records: {RecordCount}): {Message}")]
    partial void LogBatchFailed(Exception? ex, string droneId, int recordCount, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Batch processing cancelled (records: {RecordCount})")]
    partial void LogBatchCancelled(string droneId, int recordCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{DroneId}] Checkpoint {CheckpointId} ({Trigger}) completed (positions: {PositionCount}, duration: {Duration})")]
    partial void LogCheckpointCommitted(string droneId, string checkpointId, CheckpointTrigger trigger, int positionCount, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{DroneId}] Checkpoint {CheckpointId} ({Trigger}) failed: {Message}")]
    partial void LogCheckpointFailed(Exception ex, string droneId, string checkpointId, CheckpointTrigger trigger, string message);

    #endregion // Logging
}
