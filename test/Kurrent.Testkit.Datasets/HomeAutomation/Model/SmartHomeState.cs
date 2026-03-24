using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Commands;
using Kurrent.Testkit.Datasets.HomeAutomation.Contracts.Events;

namespace Kurrent.Testkit.Datasets.HomeAutomation.Model;

public class SmartHomeState {
    readonly Dictionary<string, Device>      _catalog    = [];
    readonly Dictionary<string, Guid>        _roomLookup = [];
    readonly Dictionary<string, DeviceState> _devices    = [];

    public SmartHome Home        { get; private set; } = null!;
    public long      CurrentTime { get; private set; }

    public IReadOnlyDictionary<string, DeviceState> Devices => _devices;

    public void Initialize(SmartHome home, long startTime) {
        Home        = home;
        CurrentTime = startTime;

        foreach (var room in home.Rooms) {
            foreach (var device in room.Devices) {
                _catalog[device.Id]    = device;
                _roomLookup[device.Id] = room.RoomId;
                _devices[device.Id]    = new DeviceState {
                    DeviceId         = device.Id,
                    IsBatteryPowered = device.IsBatteryPowered
                };
            }
        }
    }

    public IHomeAutomationEvent Execute(ISmartHomeCommand command, long timestamp) {
        if (!_devices.TryGetValue(command.DeviceId, out var state))
            throw new InvalidOperationException($"Unknown device: {command.DeviceId}");

        var device = _catalog[command.DeviceId];
        var roomId = _roomLookup[command.DeviceId];
        var info   = new DeviceInfo {
            Id           = device.Id,
            RoomId       = roomId,
            BatteryLevel = state.IsBatteryPowered ? state.BatteryLevel : null
        };

        return command switch {
            RecordTemperature cmd => new TemperatureReading {
                Device      = info,
                Temperature = cmd.Temperature,
                Unit        = cmd.Unit,
                Timestamp   = timestamp
            },
            RecordHumidity cmd => new HumidityReading {
                Device   = info,
                Humidity = cmd.Humidity,
                ComfortLevel = cmd.Humidity switch {
                    < 30 => ComfortLevel.Low,
                    > 60 => ComfortLevel.High,
                    _    => ComfortLevel.Optimal
                },
                Timestamp = timestamp
            },
            RecordMotion cmd => new MotionDetected {
                Device     = info,
                Confidence = cmd.Confidence,
                ZoneId     = cmd.ZoneId,
                Timestamp  = timestamp
            },
            RecordDoorStateChanged cmd => (cmd.IsOpen, cmd.LockStatus) switch {
                (true, _)                    => new DoorOpened   { Device = info, UserId = cmd.UserId, Timestamp = timestamp },
                (false, LockStatus.Locked)   => new DoorLocked  { Device = info, UserId = cmd.UserId, Timestamp = timestamp },
                (false, LockStatus.Unlocked) => state.DoorIsOpen
                    ? (IHomeAutomationEvent)new DoorClosed   { Device = info, UserId = cmd.UserId, Timestamp = timestamp }
                    : new DoorUnlocked { Device = info, UserId = cmd.UserId, Timestamp = timestamp },
                _                            => new DoorUnlocked { Device = info, UserId = cmd.UserId, Timestamp = timestamp }
            },
            RecordLightStateChanged cmd => cmd.IsOn
                ? new LightTurnedOn {
                    Device           = info,
                    Brightness       = cmd.Brightness,
                    Color            = cmd.Color,
                    PowerConsumption = cmd.Brightness * 0.6,
                    Timestamp        = timestamp
                }
                : new LightTurnedOff {
                    Device           = info,
                    PowerConsumption = 0,
                    Timestamp        = timestamp
                },
            RecordWindowStateChanged cmd => cmd.IsOpen
                ? new WindowOpened {
                    Device         = info,
                    OpenPercentage = cmd.OpenPercentage,
                    Timestamp      = timestamp
                }
                : (IHomeAutomationEvent)new WindowClosed {
                    Device    = info,
                    Timestamp = timestamp
                },
            RecordSmokeState cmd => cmd.HasSmoke
                ? new SmokeAlarmTriggered {
                    Device     = info,
                    SmokeLevel = cmd.SmokeLevel,
                    Timestamp  = timestamp
                }
                : (IHomeAutomationEvent)new SmokeAlarmCleared {
                    Device    = info,
                    Timestamp = timestamp
                },
            RecordWaterLeakState cmd => cmd.HasLeak
                ? new WaterLeakDetected {
                    Device        = info,
                    MoistureLevel = cmd.MoistureLevel,
                    Timestamp     = timestamp
                }
                : (IHomeAutomationEvent)new WaterLeakResolved {
                    Device    = info,
                    Timestamp = timestamp
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

            case DoorOpened:
                state.DoorIsOpen = true;
                state.DoorLock   = LockStatus.Unlocked;
                break;

            case DoorClosed:
                state.DoorIsOpen = false;
                break;

            case DoorLocked:
                state.DoorIsOpen = false;
                state.DoorLock   = LockStatus.Locked;
                break;

            case DoorUnlocked:
                state.DoorLock = LockStatus.Unlocked;
                break;

            case LightTurnedOn e:
                state.LightIsOn  = true;
                state.Brightness = e.Brightness;
                state.LightColor = e.Color;
                break;

            case LightTurnedOff:
                state.LightIsOn  = false;
                state.Brightness = 0;
                state.LightColor = "#000000";
                break;

            case WindowOpened e:
                state.IsWindowOpen   = true;
                state.OpenPercentage = e.OpenPercentage;
                break;

            case WindowClosed:
                state.IsWindowOpen   = false;
                state.OpenPercentage = 0;
                break;

            case SmokeAlarmTriggered:
                state.SmokeAlarmActive = true;
                break;

            case SmokeAlarmCleared:
                state.SmokeAlarmActive = false;
                break;

            case WaterLeakDetected:
                state.WaterLeakDetected = true;
                break;

            case WaterLeakResolved:
                state.WaterLeakDetected = false;
                break;
        }
    }
}
