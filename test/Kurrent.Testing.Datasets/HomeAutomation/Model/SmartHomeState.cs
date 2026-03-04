using Testing.Datasets.HomeAutomation.Contracts.Commands;
using Testing.Datasets.HomeAutomation.Contracts.Events;

namespace Testing.Datasets.HomeAutomation.Model;

public class SmartHomeState {
    readonly Dictionary<string, DeviceState> _devices = [];
    readonly Dictionary<string, Device>      _catalog = [];

    public SmartHome                              Home        { get; private set; } = null!;
    public long                                   CurrentTime { get; private set; }
    public IReadOnlyDictionary<string, DeviceState> Devices   => _devices;

    public void Initialize(SmartHome home, long startTime) {
        Home        = home;
        CurrentTime = startTime;

        foreach (var device in home.Devices) {
            _catalog[device.Id] = device;
            _devices[device.Id] = new DeviceState {
                DeviceId         = device.Id,
                DeviceType       = device.DeviceType,
                IsBatteryPowered = device.IsBatteryPowered
            };
        }
    }

    public IHomeAutomationEvent Execute(ISmartHomeCommand command, long timestamp) {
        if (!_devices.TryGetValue(command.DeviceId, out var state))
            throw new InvalidOperationException($"Unknown device: {command.DeviceId}");

        var device = _catalog[command.DeviceId];
        var info   = device.CreateDeviceInfo(state.IsBatteryPowered ? state.BatteryLevel : null);

        return command switch {
            RecordTemperature cmd => new TemperatureReading {
                Device      = info,
                Temperature = cmd.Temperature,
                Unit        = cmd.Unit,
                Timestamp   = timestamp
            },
            RecordHumidity cmd => new HumidityReading {
                Device       = info,
                Humidity     = cmd.Humidity,
                ComfortLevel = cmd.Humidity switch {
                    < 30  => ComfortLevel.Low,
                    > 60  => ComfortLevel.High,
                    _     => ComfortLevel.Optimal
                },
                Timestamp = timestamp
            },
            RecordMotion cmd => new MotionDetected {
                Device     = info,
                Confidence = cmd.Confidence,
                ZoneId     = cmd.ZoneId,
                Timestamp  = timestamp
            },
            RecordDoorOpen cmd => new DoorStateChanged {
                Device     = info,
                IsOpen     = true,
                LockStatus = LockStatus.Unlocked,
                UserId     = cmd.UserId,
                Timestamp  = timestamp
            },
            RecordDoorClose cmd => new DoorStateChanged {
                Device     = info,
                IsOpen     = false,
                LockStatus = state.DoorLock,
                UserId     = cmd.UserId,
                Timestamp  = timestamp
            },
            RecordDoorLock cmd => new DoorStateChanged {
                Device     = info,
                IsOpen     = false,
                LockStatus = LockStatus.Locked,
                UserId     = cmd.UserId,
                Timestamp  = timestamp
            },
            RecordDoorUnlock cmd => new DoorStateChanged {
                Device     = info,
                IsOpen     = state.DoorIsOpen,
                LockStatus = LockStatus.Unlocked,
                UserId     = cmd.UserId,
                Timestamp  = timestamp
            },
            RecordWindowOpen cmd => new WindowStateChanged {
                Device           = info,
                IsOpen           = true,
                OpenPercentage   = cmd.OpenPercentage,
                WeatherCondition = WeatherCondition.Unspecified,
                Timestamp        = timestamp
            },
            RecordWindowClose => new WindowStateChanged {
                Device           = info,
                IsOpen           = false,
                OpenPercentage   = 0,
                WeatherCondition = WeatherCondition.Unspecified,
                Timestamp        = timestamp
            },
            RecordLightOn cmd => new LightStateChanged {
                Device           = info,
                IsOn             = true,
                Brightness       = cmd.Brightness,
                Color            = cmd.Color,
                PowerConsumption = cmd.Brightness * 0.6,
                Timestamp        = timestamp
            },
            RecordLightOff => new LightStateChanged {
                Device           = info,
                IsOn             = false,
                Brightness       = 0,
                Color            = "#000000",
                PowerConsumption = 0,
                Timestamp        = timestamp
            },
            RecordSmokeDetected cmd => new SmokeDetected {
                Device      = info,
                HasSmoke    = true,
                SmokeLevel  = cmd.SmokeLevel,
                AlarmStatus = AlarmStatus.Active,
                Timestamp   = timestamp
            },
            RecordSmokeCleared => new SmokeDetected {
                Device      = info,
                HasSmoke    = false,
                SmokeLevel  = 0,
                AlarmStatus = AlarmStatus.Normal,
                Timestamp   = timestamp
            },
            RecordWaterLeakDetected cmd => new WaterLeakDetected {
                Device        = info,
                HasWater      = true,
                MoistureLevel = cmd.MoistureLevel,
                AlertSent     = true,
                Timestamp     = timestamp
            },
            RecordWaterLeakResolved => new WaterLeakDetected {
                Device        = info,
                HasWater      = false,
                MoistureLevel = 0,
                AlertSent     = false,
                Timestamp     = timestamp
            },
            _ => throw new InvalidOperationException($"Unknown command: {command.GetType().Name}")
        };
    }

    public void Apply(IHomeAutomationEvent evt) {
        CurrentTime = evt.Timestamp;

        if (!_devices.TryGetValue(evt.Device.Id, out var state))
            return;

        state.DrainBattery();

        switch (evt) {
            case TemperatureReading e:
                state.CurrentTemperature = e.Temperature;
                break;
            case HumidityReading e:
                state.CurrentHumidity = e.Humidity;
                break;
            case MotionDetected:
                break;
            case DoorStateChanged e:
                state.DoorIsOpen = e.IsOpen;
                state.DoorLock   = e.LockStatus;
                break;
            case WindowStateChanged e:
                state.IsWindowOpen   = e.IsOpen;
                state.OpenPercentage = e.OpenPercentage;
                break;
            case LightStateChanged e:
                state.LightIsOn  = e.IsOn;
                state.Brightness = e.Brightness;
                state.LightColor = e.Color;
                break;
            case SmokeDetected e:
                state.SmokeAlarmActive = e.HasSmoke;
                break;
            case WaterLeakDetected e:
                state.WaterLeakDetected = e.HasWater;
                break;
        }
    }
}
