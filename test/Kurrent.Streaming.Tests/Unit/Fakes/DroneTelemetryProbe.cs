using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kurrent.Streaming.Diagnostics;
using Kurrent.Streaming.Diagnostics.Metrics;

namespace Kurrent.Streaming.Tests.Fakes;

/// <summary>
/// Captures a single drone's telemetry — tracing spans and metric measurements — using only BCL
/// primitives, so tests need no MetricCollector / Diagnostics.Testing package.
/// </summary>
/// <remarks>
/// <para>
/// Drives the real <see cref="DroneMetricsObserver"/> through a <see cref="TestMeterFactory"/>, then
/// reads the resulting instruments with a <see cref="MeterListener"/>. The observer subscribes to the
/// process-global <c>Kurrent.Streaming.Core</c> diagnostic listener, so it receives events from every drone
/// running in parallel; measurements are therefore filtered to the probe's own <c>processor_id</c>.
/// Each test must use a unique processor id (see <see cref="DroneTelemetryProbe(string)"/>).
/// </para>
/// <para>
/// Subscribing the observer also flips <c>DiagnosticSource.IsEnabled("BatchProcessed")</c> on, which is
/// what makes the drone capture the batch duration timestamp — so a probe must exist before the run.
/// </para>
/// </remarks>
sealed class DroneTelemetryProbe : IDisposable {
    readonly string               _processorId;
    readonly TestMeterFactory     _meterFactory = new();
    readonly DroneMetricsObserver _observer;
    readonly Meter                _meter;
    readonly MeterListener        _meterListener;
    readonly ActivityListener     _activityListener;

    readonly ConcurrentQueue<(string Name, double Value)> _measurements = new();
    readonly ConcurrentQueue<Activity>                    _activities   = new();

    public DroneTelemetryProbe(string processorId) {
        _processorId = processorId;

        _observer = new DroneMetricsObserver(_meterFactory).Start();
        _meter    = _meterFactory.Meters.Single();

        _meterListener = new MeterListener {
            // Only enable instruments published by THIS probe's meter, so parallel tests' observers
            // (which share the global diagnostic listener) don't bleed their instruments in.
            InstrumentPublished = (instrument, listener) => {
                if (ReferenceEquals(instrument.Meter, _meter))
                    listener.EnableMeasurementEvents(instrument);
            }
        };

        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Capture(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _)   => Capture(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _)    => Capture(instrument, value, tags));
        _meterListener.Start();

        _activityListener = new ActivityListener {
            ShouldListenTo = source => source.Name == TelemetryConstants.SourceName,
            Sample         = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => {
                if (activity.GetTagItem("processor.id") as string == _processorId)
                    _activities.Enqueue(activity);
            }
        };

        ActivitySource.AddActivityListener(_activityListener);
    }

    // Filter to this probe's processor — the observer maps EVERY drone's events onto this meter, but
    // each instrument carries the originating processor_id tag.
    void Capture(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) {
        foreach (var tag in tags) {
            if (tag is { Key: "processor_id" } && tag.Value as string == _processorId) {
                _measurements.Enqueue((instrument.Name, value));
                return;
            }
        }
    }

    /// <summary>All recorded values for the named instrument, in capture order.</summary>
    public IReadOnlyList<double> Values(string instrumentName) =>
        _measurements.Where(m => m.Name == instrumentName).Select(m => m.Value).ToArray();

    /// <summary>All stopped activities (spans) with the given operation name.</summary>
    public IReadOnlyList<Activity> Activities(string operationName) =>
        _activities.Where(a => a.OperationName == operationName).ToArray();

    public void Dispose() {
        _activityListener.Dispose();
        _meterListener.Dispose();
        _observer.Dispose();
        _meterFactory.Dispose();
    }
}
