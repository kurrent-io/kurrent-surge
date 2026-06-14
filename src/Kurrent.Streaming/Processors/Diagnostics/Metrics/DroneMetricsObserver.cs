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
    readonly CheckpointsFailedCounter    _checkpointsFailed;
    readonly RecordsProcessedCounter     _recordsProcessed;
    readonly DroneErrorsCounter          _droneErrors;
    readonly UncommittedRecordsGauge     _uncommittedRecords;
    readonly UncommittedBytesGauge       _uncommittedBytes;
    readonly SourceLagBytesGauge         _sourceLagBytes;
    readonly SourceLagSecondsGauge       _sourceLagSeconds;

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
        _checkpointsFailed    = ProcessorMetrics.CreateCheckpointsFailed(meter);
        _checkpointDuration   = ProcessorMetrics.CreateCheckpointDuration(meter);
        _droneErrors          = ProcessorMetrics.CreateDroneErrors(meter);
        _uncommittedRecords   = ProcessorMetrics.CreateUncommittedRecords(meter);
        _uncommittedBytes     = ProcessorMetrics.CreateUncommittedBytes(meter);
        _sourceLagBytes       = ProcessorMetrics.CreateSourceLagBytes(meter);
        _sourceLagSeconds     = ProcessorMetrics.CreateSourceLagSeconds(meter);
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
                if (e.DurationSeconds is { } duration)
                    _recordDuration.Record(duration, e.ProcessorId);
                // Synchronous gauges export their last recorded value — push the local lag each record.
                _uncommittedRecords.Record(e.UncommittedRecords, e.ProcessorId, e.DroneId);
                _uncommittedBytes.Record(e.UncommittedBytes, e.ProcessorId, e.DroneId);
                break;

            case CheckpointCompletedEvent e:
                _checkpointsCompleted.Add(1, e.ProcessorId, e.DroneId, e.Trigger);
                _checkpointDuration.Record(e.DurationSeconds, e.ProcessorId);
                break;

            case CheckpointFailedEvent e:
                _checkpointsFailed.Add(1, e.ProcessorId, e.DroneId, e.Trigger);
                break;

            case SourceLagSampleEvent e:
                _sourceLagBytes.Record(e.LagBytes, e.ProcessorId, e.DroneId);
                _sourceLagSeconds.Record(e.LagSeconds, e.ProcessorId, e.DroneId);
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
