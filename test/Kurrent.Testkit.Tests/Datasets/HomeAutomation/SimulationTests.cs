using Kurrent.Testkit.Datasets.HomeAutomation;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;
using Kurrent.Testkit.Datasets.HomeAutomation.Simulation;

namespace Kurrent.Testkit.Tests.Datasets.HomeAutomation;

[Category("HomeAutomation")]
public class SimulationTests {
    static readonly SmartHome TestHome = BuildTestHome();

    static SmartHome BuildTestHome() {
        var ds      = HomeAutomationDataSet.Default;
        var devices = new List<Device> {
            ds.Device(DeviceType.Thermostat),
            ds.Device(DeviceType.HumiditySensor),
            ds.Device(DeviceType.SmartLight),
            ds.Device(DeviceType.MotionSensor),
            ds.Device(DeviceType.DoorLock),
            ds.Device(DeviceType.WindowSensor),
            ds.Device(DeviceType.SmokeDetector),
            ds.Device(DeviceType.WaterSensor)
        };

        return new SmartHome {
            Id        = "test-sim-home",
            Name      = "Simulation Test Home",
            CreatedAt = DateTime.UtcNow,
            Rooms = [
                new Room {
                    RoomId  = TestId.New(),
                    Name    = "Main Room",
                    Type    = RoomType.LivingRoom,
                    Devices = devices
                }
            ]
        };
    }

    static async Task<List<IHomeAutomationEvent>> Collect(HomeAutomationSimulation sim, CancellationToken ct = default) {
        var events = new List<IHomeAutomationEvent>();
        await foreach (var e in sim.WithCancellation(ct))
            events.Add(e);
        return events;
    }

    #region ->> Basic operation <<-

    [Test]
    public async Task simulation_produces_events() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .MaxEvents(50)
            .Create();

        var events = await Collect(sim);
        await Assert.That(events.Count).IsEqualTo(50);
    }

    [Test]
    public async Task simulation_respects_max_events() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .MaxEvents(25)
            .Create();

        var events = await Collect(sim);
        await Assert.That(events.Count).IsEqualTo(25);
    }

    [Test]
    public async Task simulation_runs_fast_without_pacing() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .MaxEvents(100)
            .Create();

        var sw     = System.Diagnostics.Stopwatch.StartNew();
        var events = await Collect(sim);
        sw.Stop();

        await Assert.That(events.Count).IsEqualTo(100);
        await Assert.That(sw.ElapsedMilliseconds).IsLessThan(5000);
    }

    #endregion

    #region ->> Infinite mode <<-

    [Test]
    public async Task infinite_mode_runs_until_cancelled() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .Create();

        using var cts = new CancellationTokenSource();
        var events    = new List<IHomeAutomationEvent>();

        await foreach (var e in sim.WithCancellation(cts.Token)) {
            events.Add(e);
            if (events.Count >= 50) cts.Cancel();
        }

        await Assert.That(events.Count).IsGreaterThanOrEqualTo(50);
    }

    #endregion

    #region ->> Event quality <<-

    [Test]
    public async Task events_have_increasing_timestamps() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .MaxEvents(50)
            .Create();

        var events = await Collect(sim);

        for (var i = 1; i < events.Count; i++)
            await Assert.That(events[i].Timestamp).IsGreaterThanOrEqualTo(events[i - 1].Timestamp);
    }

    [Test]
    public async Task events_reference_valid_devices() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .MaxEvents(50)
            .Create();

        var deviceIds = TestHome.Devices.Select(d => d.Id).ToHashSet();
        var events    = await Collect(sim);

        foreach (var evt in events)
            await Assert.That(deviceIds.Contains(evt.Device.Id)).IsTrue();
    }

    #endregion

    #region ->> Season effects <<-

    [Test]
    public async Task winter_produces_lower_temperatures() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .WithSeason(SeasonProfile.Winter)
            .MaxEvents(200)
            .Create();

        var events = await Collect(sim);
        var temps  = events.OfType<TemperatureReading>().Select(t => t.Temperature).ToList();

        await Assert.That(temps.Count).IsGreaterThan(0);
        await Assert.That(temps.Average()).IsLessThan(25.0);
    }

    [Test]
    public async Task summer_produces_higher_temperatures() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .WithSeason(SeasonProfile.Summer)
            .MaxEvents(200)
            .Create();

        var events = await Collect(sim);
        var temps  = events.OfType<TemperatureReading>().Select(t => t.Temperature).ToList();

        await Assert.That(temps.Count).IsGreaterThan(0);
        await Assert.That(temps.Average()).IsGreaterThan(10.0);
    }

    #endregion

    #region ->> Weather effects <<-

    [Test]
    public async Task stormy_weather_produces_higher_humidity_than_sunny() {
        var baseSeason = SeasonProfile.Summer with {
            WeatherWeights = new Dictionary<WeatherCondition, double> {
                [WeatherCondition.Stormy] = 1.0,
                [WeatherCondition.Sunny]  = 0.0,
                [WeatherCondition.Cloudy] = 0.0,
                [WeatherCondition.Rainy]  = 0.0,
                [WeatherCondition.Windy]  = 0.0
            }
        };

        var sunnySeason = SeasonProfile.Summer with {
            WeatherWeights = new Dictionary<WeatherCondition, double> {
                [WeatherCondition.Sunny]  = 1.0,
                [WeatherCondition.Stormy] = 0.0,
                [WeatherCondition.Cloudy] = 0.0,
                [WeatherCondition.Rainy]  = 0.0,
                [WeatherCondition.Windy]  = 0.0
            }
        };

        var stormySim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .WithSeason(baseSeason)
            .MaxEvents(500)
            .Create();

        var sunnySim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .WithSeason(sunnySeason)
            .MaxEvents(500)
            .Create();

        var stormyEvents = await Collect(stormySim);
        var sunnyEvents  = await Collect(sunnySim);

        var stormyHumidity = stormyEvents.OfType<HumidityReading>().Select(h => h.Humidity).ToList();
        var sunnyHumidity  = sunnyEvents.OfType<HumidityReading>().Select(h => h.Humidity).ToList();

        await Assert.That(stormyHumidity.Count).IsGreaterThan(0);
        await Assert.That(sunnyHumidity.Count).IsGreaterThan(0);
        await Assert.That(stormyHumidity.Average()).IsGreaterThan(sunnyHumidity.Average());
    }

    #endregion

    #region ->> Battery <<-

    [Test]
    public async Task battery_depletes_over_time_on_battery_devices() {
        var sim = HomeSimulator.NewSimulation()
            .ForHome(TestHome)
            .MaxEvents(500)
            .Create();

        var events = await Collect(sim);
        var batteryEvents = events
            .Where(e => e.Device.BatteryLevel.HasValue)
            .ToList();

        await Assert.That(batteryEvents.Count).IsGreaterThanOrEqualTo(2);

        var first = batteryEvents.First().Device.BatteryLevel!.Value;
        var last  = batteryEvents.Last().Device.BatteryLevel!.Value;
        await Assert.That(last).IsLessThanOrEqualTo(first);
    }

    #endregion
}
