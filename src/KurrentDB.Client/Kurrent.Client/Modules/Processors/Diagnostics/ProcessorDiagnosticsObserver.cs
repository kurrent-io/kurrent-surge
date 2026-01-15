// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kurrent.Client.Processors.Diagnostics;

/// <summary>
/// Observes processor diagnostic events and records metrics and traces.
/// Register via DI to enable observability for processor workers.
/// </summary>
/// <remarks>
/// Call <see cref="Start"/> after construction to begin listening for events.
/// </remarks>
sealed class ProcessorDiagnosticsObserver : IObserver<KeyValuePair<string, object?>>, IDisposable {
    readonly RecordsProcessedCounter _recordsProcessed;
    readonly BatchDurationHistogram _batchDuration;
    readonly CheckpointsCompletedCounter _checkpointsCompleted;
    readonly CheckpointDurationHistogram _checkpointDuration;
    readonly WorkersConnectedCounter _workersConnected;
    readonly WorkersDisconnectedCounter _workersDisconnected;

    IDisposable? _listenerSubscription;
    IDisposable? _sourceSubscription;

    /// <summary>
    /// Creates a new instance of the observer.
    /// </summary>
    /// <param name="meterFactory">Factory to create the meter for metrics.</param>
    public ProcessorDiagnosticsObserver(IMeterFactory meterFactory) {
        var meter = meterFactory.Create("Kurrent.Client.Processors");
        _recordsProcessed = ProcessorMetrics.CreateRecordsProcessed(meter);
        _batchDuration = ProcessorMetrics.CreateBatchDuration(meter);
        _checkpointsCompleted = ProcessorMetrics.CreateCheckpointsCompleted(meter);
        _checkpointDuration = ProcessorMetrics.CreateCheckpointDuration(meter);
        _workersConnected = ProcessorMetrics.CreateWorkersConnected(meter);
        _workersDisconnected = ProcessorMetrics.CreateWorkersDisconnected(meter);
    }

    /// <summary>
    /// Starts listening for processor diagnostic events.
    /// </summary>
    /// <returns>This instance for method chaining.</returns>
    public ProcessorDiagnosticsObserver Start() {
        _listenerSubscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(this));
        return this;
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

            case WorkerConnectedEvent e:
                _workersConnected.Add(1, e.ProcessorId);
                break;

            case WorkerDisconnectedEvent e:
                _workersDisconnected.Add(1, e.ProcessorId, e.Failed.ToString().ToLowerInvariant());
                break;
        }
    }

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <inheritdoc />
    public void OnError(Exception error) { }

    /// <inheritdoc />
    public void Dispose() {
        _sourceSubscription?.Dispose();
        _listenerSubscription?.Dispose();
    }

    sealed class ListenerObserver(ProcessorDiagnosticsObserver parent) : IObserver<DiagnosticListener> {
        public void OnNext(DiagnosticListener listener) {
            if (listener.Name == ProcessorDiagnostics.SourceName)
                parent._sourceSubscription = listener.Subscribe(parent);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
