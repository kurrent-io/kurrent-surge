using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;
using Testing.Datasets.HomeAutomation.Simulation;

namespace Testing.Datasets.HomeAutomation.Tests;

[Category("HomeAutomation")]
public class SimulationTests {

    static async Task<List<IHomeAutomationEvent>> Collect(HomeAutomationSimulation sim, CancellationToken ct = default) {
        var events = new List<IHomeAutomationEvent>();
        await foreach (var e in sim.WithCancellation(ct))
            events.Add(e);
        return events;
    }

    #region Basic operation

    [Test]
    public async Task simulation_produces_events() {
        var sim = HomeSimulator.NewSimulation()
            .MaxEvents(50)
            .Create();

        var events = await Collect(sim);
        await Assert.That(events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task simulation_respects_max_events() {
        var sim = HomeSimulator.NewSimulation()
            .MaxEvents(25)
            .Create();

        var events = await Collect(sim);
        await Assert.That(events.Count).IsLessThanOrEqualTo(25);
    }

    [Test]
    public async Task simulation_runs_fast_without_pacing() {
        var sim = HomeSimulator.NewSimulation()
            .MaxEvents(100)
            .Create();

        var sw     = System.Diagnostics.Stopwatch.StartNew();
        var events = await Collect(sim);
        sw.Stop();

        await Assert.That(events.Count).IsGreaterThan(0);
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(5000);
    }

    #endregion

    #region Infinite mode

    [Test]
    public async Task infinite_mode_runs_until_cancelled() {
        var sim = HomeSimulator.NewSimulation().Create();

        using var cts = new CancellationTokenSource();
        var events    = new List<IHomeAutomationEvent>();

        await foreach (var e in sim.WithCancellation(cts.Token)) {
            events.Add(e);
            if (events.Count >= 50) cts.Cancel();
        }

        await Assert.That(events.Count).IsGreaterThanOrEqualTo(50);
    }

    #endregion

    #region Event quality

    [Test]
    public async Task events_have_increasing_timestamps() {
        var sim = HomeSimulator.NewSimulation()
            .MaxEvents(50)
            .Create();

        var events = await Collect(sim);

        for (var i = 1; i < events.Count; i++)
            await Assert.That(events[i].Timestamp).IsGreaterThanOrEqualTo(events[i - 1].Timestamp);
    }

    [Test]
    public async Task events_reference_valid_devices() {
        var home = HomeAutomationDataSet.Default.Home();
        var sim  = HomeSimulator.NewSimulation()
            .ForHome(home)
            .MaxEvents(50)
            .Create();

        var deviceIds = home.Devices.Select(d => d.Id).ToHashSet();
        var events    = await Collect(sim);

        foreach (var evt in events)
            await Assert.That(deviceIds.Contains(evt.Device.Id)).IsTrue();
    }

    #endregion

    #region Season effects

    [Test]
    public async Task winter_produces_lower_temperatures() {
        var sim = HomeSimulator.NewSimulation()
            .WithSeason(SeasonProfile.Winter)
            .MaxEvents(200)
            .Create();

        var events = await Collect(sim);
        var temps  = events.OfType<TemperatureReading>().Select(t => t.Temperature).ToList();

        if (temps.Count > 0)
            await Assert.That(temps.Average()).IsLessThan(25.0);
    }

    [Test]
    public async Task summer_produces_higher_temperatures() {
        var sim = HomeSimulator.NewSimulation()
            .WithSeason(SeasonProfile.Summer)
            .MaxEvents(200)
            .Create();

        var events = await Collect(sim);
        var temps  = events.OfType<TemperatureReading>().Select(t => t.Temperature).ToList();

        if (temps.Count > 0)
            await Assert.That(temps.Average()).IsGreaterThan(10.0);
    }

    #endregion

    #region Battery

    [Test]
    public async Task battery_depletes_over_time_on_battery_devices() {
        var sim = HomeSimulator.NewSimulation()
            .MaxEvents(500)
            .Create();

        var events = await Collect(sim);
        var batteryEvents = events
            .Where(e => e.Device.IsBatteryPowered && e.Device.BatteryLevel.HasValue)
            .ToList();

        if (batteryEvents.Count >= 2) {
            var first = batteryEvents.First().Device.BatteryLevel!.Value;
            var last  = batteryEvents.Last().Device.BatteryLevel!.Value;
            await Assert.That(last).IsLessThanOrEqualTo(first);
        }
    }

    #endregion
}
