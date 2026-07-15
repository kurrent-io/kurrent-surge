using System.Diagnostics.Metrics;

namespace Kurrent.Surge.Streaming.Tests.Fakes;

/// <summary>
/// Minimal <see cref="IMeterFactory"/> for tests — no DI container required. Tracks the meters it
/// creates so a <see cref="MeterListener"/> can be scoped to exactly the instruments published by one
/// factory instance, isolating measurements from other tests sharing the global metrics plumbing.
/// </summary>
sealed class TestMeterFactory : IMeterFactory {
    readonly List<Meter> _meters = [];

    public IReadOnlyList<Meter> Meters => _meters;

    public Meter Create(MeterOptions options) {
        var meter = new Meter(options);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() {
        foreach (var meter in _meters)
            meter.Dispose();

        _meters.Clear();
    }
}
