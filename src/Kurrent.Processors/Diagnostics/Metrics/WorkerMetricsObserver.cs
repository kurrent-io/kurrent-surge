using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kurrent.Processors.Diagnostics.Metrics;

/// <summary>
/// Observes worker diagnostic events and records metrics.
/// Register via DI to enable observability for processor workers.
/// </summary>
/// <remarks>
/// Call <see cref="Start"/> after construction to begin listening for events.
/// </remarks>
sealed class WorkerMetricsObserver : IObserver<KeyValuePair<string, object?>>, IDisposable {
    readonly BatchDurationHistogram      _batchDuration;
    readonly CheckpointDurationHistogram _checkpointDuration;
    readonly CheckpointsCompletedCounter _checkpointsCompleted;
    readonly RecordsProcessedCounter     _recordsProcessed;
    readonly WorkersConnectedCounter     _workersConnected;
    readonly WorkerErrorsCounter         _workerErrors;

    IDisposable? _listenerSubscription;
    IDisposable? _sourceSubscription;

    /// <summary>
    /// Creates a new instance of the observer.
    /// </summary>
    /// <param name="meterFactory">Factory to create the meter for metrics.</param>
    public WorkerMetricsObserver(IMeterFactory meterFactory) {
        var meter = meterFactory.Create("Kurrent.Client.Processors");
        _recordsProcessed     = ProcessorMetrics.CreateRecordsProcessed(meter);
        _batchDuration        = ProcessorMetrics.CreateBatchDuration(meter);
        _checkpointsCompleted = ProcessorMetrics.CreateCheckpointsCompleted(meter);
        _checkpointDuration   = ProcessorMetrics.CreateCheckpointDuration(meter);
        _workersConnected     = ProcessorMetrics.CreateWorkersConnected(meter);
        _workerErrors         = ProcessorMetrics.CreateWorkerErrors(meter);
    }

    /// <inheritdoc />
    public void Dispose() {
        _sourceSubscription?.Dispose();
        _listenerSubscription?.Dispose();
    }

    /// <inheritdoc />
    public void OnNext(KeyValuePair<string, object?> evt) {
        // Note: Tracing spans are managed by BatchScope/CheckpointScope directly.
        // This observer only records metrics.
        switch (evt.Value) {
            case BatchProcessedEvent e:
                _recordsProcessed.Add(e.BatchSize, e.ProcessorId, e.WorkerId);
                _batchDuration.Record(e.DurationMs, e.ProcessorId);
                break;

            case CheckpointCompletedEvent e:
                _checkpointsCompleted.Add(1, e.ProcessorId, e.WorkerId);
                _checkpointDuration.Record(e.DurationMs, e.ProcessorId);
                break;

            case WorkerProcessingEvent e:
                _workersConnected.Add(1, e.ProcessorId);
                break;

            case WorkerErrorEvent e:
                _workerErrors.Add(1, e.ProcessorId);
                break;
        }
    }

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <inheritdoc />
    public void OnError(Exception error) { }

    /// <summary>
    /// Starts listening for worker diagnostic events.
    /// </summary>
    /// <returns>This instance for method chaining.</returns>
    public WorkerMetricsObserver Start() {
        _listenerSubscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(this));
        return this;
    }

    sealed class ListenerObserver(WorkerMetricsObserver parent) : IObserver<DiagnosticListener> {
        public void OnNext(DiagnosticListener listener) {
            if (listener.Name == TelemetryConstants.SourceName)
                parent._sourceSubscription = listener.Subscribe(parent);
        }

        public void OnCompleted()            { }
        public void OnError(Exception error) { }
    }
}
