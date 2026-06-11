using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kurrent.Streaming.Diagnostics.Metrics;

/// <summary>
/// Observes drone diagnostic events and records metrics.
/// Register via DI to enable observability for processor drones.
/// </summary>
/// <remarks>
/// Call <see cref="Start"/> after construction to begin listening for events.
/// </remarks>
sealed class DroneMetricsObserver : IObserver<KeyValuePair<string, object?>>, IDisposable {
    readonly RecordDurationHistogram     _recordDuration;
    readonly CheckpointDurationHistogram _checkpointDuration;
    readonly CheckpointsCompletedCounter _checkpointsCompleted;
    readonly RecordsProcessedCounter     _recordsProcessed;
    readonly DronesConnectedCounter      _dronesConnected;
    readonly DroneErrorsCounter          _droneErrors;

    IDisposable? _listenerSubscription;
    IDisposable? _sourceSubscription;

    /// <summary>
    /// Creates a new instance of the observer.
    /// </summary>
    /// <param name="meterFactory">Factory to create the meter for metrics.</param>
    public DroneMetricsObserver(IMeterFactory meterFactory) {
        var meter = meterFactory.Create("Kurrent.Client.Processors");
        _recordsProcessed     = ProcessorMetrics.CreateRecordsProcessed(meter);
        _recordDuration       = ProcessorMetrics.CreateRecordDuration(meter);
        _checkpointsCompleted = ProcessorMetrics.CreateCheckpointsCompleted(meter);
        _checkpointDuration   = ProcessorMetrics.CreateCheckpointDuration(meter);
        _dronesConnected      = ProcessorMetrics.CreateDronesConnected(meter);
        _droneErrors          = ProcessorMetrics.CreateDroneErrors(meter);
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
            case RecordProcessedEvent e:
                _recordsProcessed.Add(1, e.ProcessorId, e.DroneId);
                if (e.DurationMs is { } duration)
                    _recordDuration.Record(duration, e.ProcessorId);
                break;

            case CheckpointCompletedEvent e:
                _checkpointsCompleted.Add(1, e.ProcessorId, e.DroneId);
                _checkpointDuration.Record(e.DurationMs, e.ProcessorId);
                break;

            case DroneProcessingEvent e:
                _dronesConnected.Add(1, e.ProcessorId);
                break;

            case DroneErrorEvent e:
                _droneErrors.Add(1, e.ProcessorId);
                break;
        }
    }

    /// <inheritdoc />
    public void OnCompleted() { }

    /// <inheritdoc />
    public void OnError(Exception error) { }

    /// <summary>
    /// Starts listening for drone diagnostic events.
    /// </summary>
    /// <returns>This instance for method chaining.</returns>
    public DroneMetricsObserver Start() {
        _listenerSubscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(this));
        return this;
    }

    sealed class ListenerObserver(DroneMetricsObserver parent) : IObserver<DiagnosticListener> {
        public void OnNext(DiagnosticListener listener) {
            if (listener.Name == TelemetryConstants.SourceName)
                parent._sourceSubscription = listener.Subscribe(parent);
        }

        public void OnCompleted()            { }
        public void OnError(Exception error) { }
    }
}
