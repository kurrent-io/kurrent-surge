using Bogus;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Commands;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;
using Kurrent.Testkit.Datasets.HomeAutomation.Model;

namespace Kurrent.Testkit.Datasets.HomeAutomation.Simulation;

public static class HomeSimulator {
    public static HomeAutomationSimulationBuilder NewSimulation() => new();
}

public class HomeAutomationSimulationBuilder {
    SmartHome?     _home;
    int            _maxEvents;
    TimeSpan       _pacing  = TimeSpan.Zero;
    SeasonProfile  _season  = SeasonProfile.Summer;
    DateTimeOffset _startAt = DateTimeOffset.UtcNow;

    public HomeAutomationSimulationBuilder ForHome(SmartHome home) {
        _home = home;
        return this;
    }

    public HomeAutomationSimulationBuilder WithSeason(SeasonProfile profile) {
        _season = profile;
        return this;
    }

    public HomeAutomationSimulationBuilder WithPacing(TimeSpan pacing) {
        _pacing = pacing;
        return this;
    }

    public HomeAutomationSimulationBuilder MaxEvents(int count) {
        _maxEvents = count;
        return this;
    }

    public HomeAutomationSimulationBuilder StartAt(DateTimeOffset time) {
        _startAt = time;
        return this;
    }

    public HomeAutomationSimulation Create() {
        var faker = new Faker();
        var home  = _home ?? HomeAutomationDataSet.Default.Home();

        return new HomeAutomationSimulation(
            home, _season, _maxEvents,
            _startAt, faker
        ) {
            Pacing = _pacing
        };
    }
}

public class HomeAutomationSimulation : IAsyncEnumerable<IHomeAutomationEvent> {
    readonly Faker          _faker;
    readonly SmartHome      _home;
    readonly int            _maxEvents;
    readonly SeasonProfile  _season;
    readonly DateTimeOffset _startAt;

    internal HomeAutomationSimulation(
        SmartHome      home,
        SeasonProfile season,
        int maxEvents,
        DateTimeOffset startAt,
        Faker         faker
    ) {
        _home      = home;
        _season    = season;
        _maxEvents = maxEvents;
        _startAt   = startAt;
        _faker     = faker;
    }

    public TimeSpan Pacing { get; init; }

    public async IAsyncEnumerator<IHomeAutomationEvent> GetAsyncEnumerator(CancellationToken ct = default) {
        var state = new SmartHomeState();

        state.Initialize(_home, _startAt.ToUnixTimeMilliseconds());

        var emitted = 0;

        while (!ct.IsCancellationRequested) {
            var advanceMinutes = _faker.Random.Number(1, 10);
            var timestamp      = state.CurrentTime + advanceMinutes * 60_000L;
            var hour           = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).Hour;

            var weather  = SelectWeather(_faker);
            var commands = SelectCommands(state, hour, weather, _faker);

            foreach (var command in commands) {
                var evt = state.Execute(command, timestamp);
                state.Apply(evt);
                yield return evt;

                await Pace(ct);

                emitted++;
                if (_maxEvents > 0 && emitted >= _maxEvents) yield break;
                if (ct.IsCancellationRequested) yield break;
            }
        }
    }

    List<ISmartHomeCommand> SelectCommands(SmartHomeState state, int hour, WeatherCondition weather, Faker faker) {
        var commands = new List<ISmartHomeCommand>();
        var devices  = _home.Devices.ToList();
        var period   = GetPeriod(hour);

        var (tempOffset, humidityOffset, windowCloseFactor) = WeatherModifiers.For(weather);

        foreach (var device in devices) {
            var ds = state.Devices[device.Id];

            switch (device) {
                case Thermostat thermo when ShouldEmit(period == Period.Night ? 0.3 : 0.6):
                    var baseTemp = faker.Random.Double(_season.MinOutdoorTemp, _season.MaxOutdoorTemp) + tempOffset;
                    var indoor   = baseTemp + faker.Random.Double(0, 5);
                    var clamped  = Math.Round(Math.Clamp(indoor, thermo.MinOperatingTemp, thermo.MaxOperatingTemp), thermo.MeasurementPrecision);
                    commands.Add(new RecordTemperature { DeviceId = device.Id, Temperature = clamped, Unit = thermo.PreferredUnit });
                    break;

                case HumiditySensor humid when ShouldEmit(period == Period.Night ? 0.3 : 0.5):
                    var humidity = Math.Round(Math.Clamp(_season.BaseHumidity + humidityOffset + faker.Random.Double(-15, 15), 0, 100), humid.MeasurementPrecision);
                    commands.Add(new RecordHumidity { DeviceId = device.Id, Humidity = humidity });
                    break;

                case MotionSensor when period == Period.Daytime && ShouldEmit(0.4):
                case MotionSensor when period == Period.Evening && ShouldEmit(0.15):
                    var motion = (MotionSensor)device;
                    commands.Add(
                        new RecordMotion { DeviceId = device.Id, Confidence = Math.Round(faker.Random.Double(0.5), 2), ZoneId = faker.Random.Number(1, motion.ZoneCount) }
                    );

                    break;

                case SmartLight light when period == Period.Evening && !ds.LightIsOn && ShouldEmit(0.5):
                    var brightness = faker.Random.Number((int)(light.MaxBrightness * 0.6), light.MaxBrightness);
                    commands.Add(new RecordLightStateChanged { DeviceId = device.Id, IsOn = true, Brightness = brightness, Color = "#FFD700" });
                    break;

                case SmartLight when period == Period.Daytime && ds.LightIsOn && ShouldEmit(0.3):
                    commands.Add(new RecordLightStateChanged { DeviceId = device.Id, IsOn = false, Brightness = 0, Color = "#000000" });
                    break;

                case SmartLight when period == Period.Night && ds.LightIsOn && ShouldEmit(0.4):
                    commands.Add(new RecordLightStateChanged { DeviceId = device.Id, IsOn = false, Brightness = 0, Color = "#000000" });
                    break;

                case DoorLock when period == Period.Evening && !ds.DoorLock.Equals(LockStatus.Locked) && ShouldEmit(0.3):
                    commands.Add(new RecordDoorStateChanged { DeviceId = device.Id, IsOpen = false, LockStatus = LockStatus.Locked, UserId = faker.Internet.UserName() });
                    break;

                case DoorLock when period == Period.Daytime && ShouldEmit(0.08):
                    commands.Add(
                        ds.DoorIsOpen
                            ? new RecordDoorStateChanged { DeviceId = device.Id, IsOpen = false, LockStatus = ds.DoorLock, UserId = faker.Internet.UserName() }
                            : new RecordDoorStateChanged { DeviceId = device.Id, IsOpen = true, LockStatus = LockStatus.Unlocked, UserId = faker.Internet.UserName() }
                    );

                    break;

                case WindowSensor window when period == Period.Daytime && !ds.IsWindowOpen && ShouldEmit(_season.WindowOpenProbability / windowCloseFactor):
                    var openPct = window.SupportsPartialOpen ? faker.Random.Number(20, 100) : 100;
                    commands.Add(new RecordWindowStateChanged { DeviceId = device.Id, IsOpen = true, OpenPercentage = openPct });
                    break;

                case WindowSensor when period == Period.Evening && ds.IsWindowOpen && ShouldEmit(0.4):
                    commands.Add(new RecordWindowStateChanged { DeviceId = device.Id, IsOpen = false, OpenPercentage = 0 });
                    break;

                case SmokeDetector smoke when !ds.SmokeAlarmActive && ShouldEmit(0.002):
                    commands.Add(new RecordSmokeState { DeviceId = device.Id, HasSmoke = true, SmokeLevel = faker.Random.Number(smoke.AlarmThreshold / 2, smoke.AlarmThreshold) });
                    break;

                case SmokeDetector when ds.SmokeAlarmActive && ShouldEmit(0.5):
                    commands.Add(new RecordSmokeState { DeviceId = device.Id, HasSmoke = false, SmokeLevel = 0 });
                    break;

                case WaterSensor water when !ds.WaterLeakDetected && ShouldEmit(weather is WeatherCondition.Rainy or WeatherCondition.Stormy ? 0.001 * 1.5 : 0.001):
                    commands.Add(new RecordWaterLeakState { DeviceId = device.Id, HasLeak = true, MoistureLevel = faker.Random.Number(water.MoistureThreshold, 100) });
                    break;

                case WaterSensor when ds.WaterLeakDetected && ShouldEmit(0.3):
                    commands.Add(new RecordWaterLeakState { DeviceId = device.Id, HasLeak = false, MoistureLevel = 0 });
                    break;
            }
        }

        if (commands.Count == 0) {
            var anyThermo = devices.OfType<Thermostat>().FirstOrDefault();
            if (anyThermo is not null) {
                var temp = Math.Round(
                    faker.Random.Double(_season.MinOutdoorTemp, _season.MaxOutdoorTemp) + faker.Random.Double(0, 5),
                    anyThermo.MeasurementPrecision
                );
                commands.Add(new RecordTemperature {
                    DeviceId    = anyThermo.Id,
                    Temperature = Math.Clamp(temp, anyThermo.MinOperatingTemp, anyThermo.MaxOperatingTemp),
                    Unit        = anyThermo.PreferredUnit
                });
            }
        }

        return commands;

        bool ShouldEmit(double probability) => faker.Random.Bool((float)probability);
    }

    WeatherCondition SelectWeather(Faker faker) {
        var totalWeight = _season.WeatherWeights.Values.Sum();
        var roll        = faker.Random.Double(0, totalWeight);
        var running     = 0.0;

        foreach (var (condition, weight) in _season.WeatherWeights) {
            running += weight;
            if (roll <= running) return condition;
        }

        return _season.WeatherWeights.Keys.First();
    }

    Period GetPeriod(int hour) {
        if (hour >= _season.DaylightStartHour && hour < _season.DaylightEndHour)
            return Period.Daytime;

        if (hour >= _season.DaylightEndHour && hour < _season.DaylightEndHour + 3)
            return Period.Evening;

        return Period.Night;
    }

    async ValueTask Pace(CancellationToken ct) {
        if (Pacing == TimeSpan.Zero) return;

        try {
            await Task.Delay(Pacing, ct);
        }
        catch (OperationCanceledException) { }
    }

    enum Period {
        Daytime,
        Evening,
        Night
    }
}
