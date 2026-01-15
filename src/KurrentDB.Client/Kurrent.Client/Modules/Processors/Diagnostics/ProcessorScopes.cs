// ReSharper disable CheckNamespace

using System.Diagnostics;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Diagnostics;

/// <summary>
/// Tracks batch processing duration and tracing span. Call <see cref="Complete"/> when done.
/// Manages both metrics (via DiagnosticSource) and tracing (via Activity).
/// </summary>
public struct BatchScope : IDisposable {
    readonly string _processorId;
    readonly string _workerId;
    readonly long _startTimestamp;
    readonly Activity? _activity;

    bool _completed;

    internal BatchScope(string processorId, string workerId, long startTimestamp, Activity? activity) {
        _processorId = processorId;
        _workerId = workerId;
        _startTimestamp = startTimestamp;
        _activity = activity;
        _completed = false;
    }

    /// <summary>
    /// Marks the batch as complete with success status.
    /// </summary>
    /// <param name="batchSize">Number of records in the batch.</param>
    /// <param name="totalRecords">Total records processed so far.</param>
    public void Complete(int batchSize, long totalRecords) {
        if (_completed) return;
        _completed = true;

        // Record metrics via DiagnosticSource
        if (_startTimestamp != 0) {
            var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
            ProcessorDiagnostics.WriteBatchProcessed(new BatchProcessedEvent(
                _processorId,
                _workerId,
                batchSize,
                totalRecords,
                elapsed.TotalMilliseconds
            ));
        }

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

        if (_activity is not null) {
            _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message }
            }));
            _activity.Dispose();
        }
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

/// <summary>
/// Tracks checkpoint duration and tracing span. Call <see cref="Complete"/> when done.
/// Manages both metrics (via DiagnosticSource) and tracing (via Activity).
/// </summary>
public struct CheckpointScope : IDisposable {
    readonly string _processorId;
    readonly string _workerId;
    readonly long _startTimestamp;
    readonly Activity? _activity;

    bool _completed;

    internal CheckpointScope(string processorId, string workerId, long startTimestamp, Activity? activity) {
        _processorId = processorId;
        _workerId = workerId;
        _startTimestamp = startTimestamp;
        _activity = activity;
        _completed = false;
    }

    /// <summary>
    /// Marks the checkpoint as complete with success status.
    /// </summary>
    /// <param name="barrierId">The barrier ID for the checkpoint.</param>
    /// <param name="partitionCount">Number of partitions in the checkpoint.</param>
    public void Complete(LogPosition barrierId, int partitionCount) {
        if (_completed) return;
        _completed = true;

        // Record metrics via DiagnosticSource
        if (_startTimestamp != 0) {
            var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
            ProcessorDiagnostics.WriteCheckpointCompleted(new CheckpointCompletedEvent(
                _processorId,
                _workerId,
                barrierId,
                partitionCount,
                elapsed.TotalMilliseconds
            ));
        }

        // Complete the tracing span
        if (_activity is not null) {
            _activity.SetTag("checkpoint.barrier_id", barrierId.ToString());
            _activity.SetTag("checkpoint.partition_count", partitionCount);
            _activity.SetStatus(ActivityStatusCode.Ok);
            _activity.Dispose();
        }
    }

    /// <summary>
    /// Marks the checkpoint as failed with error status.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    public void Fail(Exception exception) {
        if (_completed) return;
        _completed = true;

        if (_activity is not null) {
            _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            _activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message }
            }));
            _activity.Dispose();
        }
    }

    /// <summary>
    /// Disposes the scope. If not completed, marks as error.
    /// </summary>
    public void Dispose() {
        if (_completed) return;
        _completed = true;

        _activity?.SetStatus(ActivityStatusCode.Error, "Scope disposed without completion");
        _activity?.Dispose();
    }
}
