using Kurrent.Surge.Processors;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kurrent.Toolkit.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Surge.Streaming.Tests.Fakes;

/// <summary>
/// Captures a single drone's telemetry — tracing spans and metric measurements — using only BCL
/// primitives, so tests need no MetricCollector / Diagnostics.Testing package.
/// </summary>
/// <remarks>
/// <para>
/// Builds the real <see cref="ProcessorTelemetryObserver"/> over a <see cref="TestMeterFactory"/> and
/// exposes it as <see cref="Observer"/> — register it on the drone under test. Instruments are read
/// back with a <see cref="MeterListener"/> scoped to this probe's meter, so parallel tests cannot
/// bleed measurements into one another.
/// </para>
/// <para>
/// Spans still travel the process-global <c>ActivitySource</c>, so activities are filtered by the
/// probe's own <c>processor.id</c> tag. Each test must use a unique processor id
/// (see <see cref="DroneTelemetryProbe(string)"/>).
/// </para>
/// </remarks>
sealed class DroneTelemetryProbe : IDisposable {
    readonly string           _processorId;
    readonly TestMeterFactory _meterFactory = new();
    readonly Meter            _meter;
    readonly MeterListener    _meterListener;
    readonly ActivityListener _activityListener;

    readonly ConcurrentQueue<(string Name, double Value)> _measurements = new();
    readonly ConcurrentQueue<Activity>                    _activities   = new();

    /// <summary>
    /// The observer to register on the drone under test — the same composite the shipping
    /// composition roots build.
    /// </summary>
    public ProcessorTelemetryObserver Observer { get; }

    public DroneTelemetryProbe(string processorId) {
        _processorId = processorId;

        Observer = new ProcessorTelemetryObserver(NullLogger.Instance, _meterFactory);
        _meter   = _meterFactory.Meters.Single();

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

    // The meter is already per-probe, but instruments still carry processor_id — filter on it so a
    // probe shared across drones in one test reports only the drone it was pointed at.
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
        _meterFactory.Dispose();
    }
}
