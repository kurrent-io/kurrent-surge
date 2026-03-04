using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Commands;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Simulation;

public static class HomeSimulator {
    public static HomeAutomationSimulationBuilder NewSimulation() => new();
}

public class HomeAutomationSimulationBuilder {
    SmartHome?     _home;
    SeasonProfile  _season   = SeasonProfile.Summer;
    TimeSpan       _pacing   = TimeSpan.Zero;
    int            _maxEvents;
    DateTimeOffset _startAt  = DateTimeOffset.UtcNow;

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

        return new HomeAutomationSimulation(home, _season, _maxEvents, _startAt, faker) {
            Pacing = _pacing
        };
    }
}

public class HomeAutomationSimulation : IAsyncEnumerable<IHomeAutomationEvent> {
    readonly SmartHome     _home;
    readonly SeasonProfile _season;
    readonly int           _maxEvents;
    readonly DateTimeOffset _startAt;
    readonly Faker          _faker;

    internal HomeAutomationSimulation(SmartHome home, SeasonProfile season, int maxEvents,
        DateTimeOffset startAt, Faker faker) {
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

            var commands = SelectCommands(state, hour, _faker);

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

    List<ISmartHomeCommand> SelectCommands(SmartHomeState state, int hour, Faker faker) {
        var commands = new List<ISmartHomeCommand>();
        var devices  = _home.Devices;
        var period   = GetPeriod(hour);

        foreach (var device in devices) {
            var ds = state.Devices[device.Id];

            switch (device) {
                case Thermostat when ShouldEmit(period == Period.Night ? 0.3 : 0.6):
                    var baseTemp  = faker.Random.Double(_season.MinOutdoorTemp, _season.MaxOutdoorTemp);
                    var indoor    = baseTemp + faker.Random.Double(0, 5);
                    var clamped   = Math.Round(Math.Clamp(indoor, 5, 45), 1);
                    commands.Add(new RecordTemperature { DeviceId = device.Id, Temperature = clamped, Unit = TemperatureUnit.Celsius });
                    break;

                case HumiditySensor when ShouldEmit(period == Period.Night ? 0.3 : 0.5):
                    var humidity = Math.Round(Math.Clamp(_season.BaseHumidity + faker.Random.Double(-15, 15), 0, 100), 1);
                    commands.Add(new RecordHumidity { DeviceId = device.Id, Humidity = humidity });
                    break;

                case MotionSensor when period == Period.Daytime && ShouldEmit(0.4):
                case MotionSensor when period == Period.Evening && ShouldEmit(0.15):
                    commands.Add(new RecordMotion { DeviceId = device.Id, Confidence = Math.Round(faker.Random.Double(0.5, 1.0), 2), ZoneId = faker.Random.Number(1, 4) });
                    break;

                case SmartLight when period == Period.Evening && !ds.LightIsOn && ShouldEmit(0.5):
                    commands.Add(new RecordLightOn { DeviceId = device.Id, Brightness = faker.Random.Number(60, 100), Color = "#FFD700" });
                    break;
                case SmartLight when period == Period.Daytime && ds.LightIsOn && ShouldEmit(0.3):
                    commands.Add(new RecordLightOff { DeviceId = device.Id });
                    break;
                case SmartLight when period == Period.Night && ds.LightIsOn && ShouldEmit(0.4):
                    commands.Add(new RecordLightOff { DeviceId = device.Id });
                    break;

                case DoorLock when period == Period.Evening && !ds.DoorLock.Equals(LockStatus.Locked) && ShouldEmit(0.3):
                    commands.Add(new RecordDoorLock { DeviceId = device.Id, UserId = faker.Internet.UserName() });
                    break;
                case DoorLock when period == Period.Daytime && ShouldEmit(0.08):
                    commands.Add(ds.DoorIsOpen
                        ? new RecordDoorClose { DeviceId = device.Id, UserId = faker.Internet.UserName() }
                        : new RecordDoorOpen  { DeviceId = device.Id, UserId = faker.Internet.UserName() });
                    break;

                case WindowSensor when period == Period.Daytime && !ds.IsWindowOpen && ShouldEmit(_season.WindowOpenProbability):
                    commands.Add(new RecordWindowOpen { DeviceId = device.Id, OpenPercentage = faker.Random.Number(20, 100) });
                    break;
                case WindowSensor when period == Period.Evening && ds.IsWindowOpen && ShouldEmit(0.4):
                    commands.Add(new RecordWindowClose { DeviceId = device.Id });
                    break;

                case SmokeDetector when !ds.SmokeAlarmActive && ShouldEmit(0.002):
                    commands.Add(new RecordSmokeDetected { DeviceId = device.Id, SmokeLevel = faker.Random.Number(20, 80) });
                    break;
                case SmokeDetector when ds.SmokeAlarmActive && ShouldEmit(0.5):
                    commands.Add(new RecordSmokeCleared { DeviceId = device.Id });
                    break;

                case WaterSensor when !ds.WaterLeakDetected && ShouldEmit(0.001):
                    commands.Add(new RecordWaterLeakDetected { DeviceId = device.Id, MoistureLevel = faker.Random.Number(10, 90) });
                    break;
                case WaterSensor when ds.WaterLeakDetected && ShouldEmit(0.3):
                    commands.Add(new RecordWaterLeakResolved { DeviceId = device.Id });
                    break;
            }
        }

        if (commands.Count == 0) {
            var anyThermo = devices.FirstOrDefault(d => d is Thermostat);
            if (anyThermo is not null) {
                var temp = Math.Round(faker.Random.Double(_season.MinOutdoorTemp, _season.MaxOutdoorTemp) + faker.Random.Double(0, 5), 1);
                commands.Add(new RecordTemperature { DeviceId = anyThermo.Id, Temperature = Math.Clamp(temp, 5, 45), Unit = TemperatureUnit.Celsius });
            }
        }

        return commands;

        bool ShouldEmit(double probability) => faker.Random.Bool((float)probability);
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
        try { await Task.Delay(Pacing, ct); }
        catch (OperationCanceledException) { }
    }

    enum Period { Daytime, Evening, Night }
}
