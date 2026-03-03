# HomeAutomation Simulation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build an event-sourced IoT simulation engine for the HomeAutomation dataset that streams realistic
`IHomeAutomationEvent` via `IAsyncEnumerable`, with command-event pipeline, seasonal profiles, and battery drain.

**Architecture:** Each tick advances the clock, selects commands probabilistically based on time-of-day and season,
executes them against `SmartHomeState` to produce events, applies events to update state, and yields to the consumer.
Infinite streaming or capped by max event count.

**Tech Stack:** C# 14, Bogus, TUnit, `IAsyncEnumerable<T>`, immutable records, event-sourced state pattern.

**Design doc:** [home-automation-simulation-design.md](2026-03-03-home-automation-simulation-design.md)

## Contents

- [Task 1: Evolve Room and SmartHome to records](#task-1-evolve-room-and-smarthome-to-records)
- [Task 2: Evolve Device hierarchy to records](#task-2-evolve-device-hierarchy-to-records)
- [Task 3: Update Fakers for record types](#task-3-update-fakers-for-record-types)
- [Task 4: Update DataSet and FakerExtensions](#task-4-update-dataset-and-fakerextensions)
- [Task 5: Build verify and commit model evolution](#task-5-build-verify-and-commit-model-evolution)
- [Task 6: Add commands](#task-6-add-commands)
- [Task 7: Add DeviceState and SmartHomeState](#task-7-add-devicestate-and-smarthomestate)
- [Task 8: Add SeasonProfile](#task-8-add-seasonprofile)
- [Task 9: Build simulation engine](#task-9-build-simulation-engine)
- [Task 10: Write model and DataSet tests](#task-10-write-model-and-dataset-tests)
- [Task 11: Write state tests](#task-11-write-state-tests)
- [Task 12: Write simulation tests](#task-12-write-simulation-tests)
- [Task 13: Final verification](#task-13-final-verification)

## Conventions

All files live under `test/Kurrent.Testing.Datasets/HomeAutomation/`. Root namespace: `Testing.Datasets.HomeAutomation`.
Model types: `.Model` sub-namespace. Contracts: `.Contracts.Events` and `.Contracts.Commands`. Simulation:
`.Simulation`. Tests: `Testing.Datasets.HomeAutomation.Tests`. Category attribute: `[Category("HomeAutomation")]`.

TUnit test runner (not `dotnet test`):

```sh
dotnet run --project test/Kurrent.Testing.Datasets/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=HomeAutomation]"
```

Full solution build:

```sh
dotnet build Kurrent.Client.slnx
```

---

### Task 1: Evolve Room and SmartHome to records

**Files:**

- Modify: `HomeAutomation/Model/Room.cs`
- Modify: `HomeAutomation/Model/SmartHome.cs`

**Step 1: Convert Room to record**

Replace the entire `Room.cs` content. Remove the parameterized constructor and parameterless constructor. Change
`{ get; set; }` to `{ get; init; }` with `required`. Remove ReSharper suppressions (they existed for the mutable
setters).

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public record Room {
    public required Guid     RoomId { get; init; }
    public required string   Name   { get; init; }
    public required RoomType Type   { get; init; }
}
```

**Step 2: Convert SmartHome to record**

Replace `SmartHome.cs`. Change `class` to `record`, `{ get; set; }` to `{ get; init; }`, add `required`.

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public record SmartHome {
    public required string       Id        { get; init; }
    public required string       Name      { get; init; }
    public required List<Room>   Rooms     { get; init; }
    public required List<Device> Devices   { get; init; }
    public required DateTime     CreatedAt { get; init; }
}
```

**Step 3: Fix Room constructor calls**

Every `new Room(guid, name, type)` call must become an object initializer. Known locations:

- `Fakers/RoomFaker.cs`: `new Room(Guid.NewGuid(), roomName, roomType)` becomes
  `new Room { RoomId = Guid.NewGuid(), Name = roomName, Type = roomType }`
- `Fakers/HomeFaker.cs`: three `new Room(...)` calls in `baseRooms`
- `HomeAutomationDataSet.cs`: `Devices()` method `GenerateDefaultRooms()` local function

**Step 4: Build**

```sh
dotnet build Kurrent.Client.slnx
```

Expected: `Build succeeded`. If errors remain, fix any remaining `new Room(...)` calls or `set` usages on SmartHome
properties.

---

### Task 2: Evolve Device hierarchy to records

**Files:**

- Modify: `HomeAutomation/Model/Device.cs`
- Modify: `HomeAutomation/Model/Thermostat.cs`
- Modify: `HomeAutomation/Model/SmartLight.cs`
- Modify: `HomeAutomation/Model/DoorLock.cs`
- Modify: `HomeAutomation/Model/MotionSensor.cs`
- Modify: `HomeAutomation/Model/SmokeDetector.cs`
- Modify: `HomeAutomation/Model/HumiditySensor.cs`
- Modify: `HomeAutomation/Model/WindowSensor.cs`
- Modify: `HomeAutomation/Model/WaterSensor.cs`

**Step 1: Convert Device to abstract record**

Replace `Device.cs`. Remove the constructor. Keep `CreateDeviceInfo`. Add `Humanizer` import.

```csharp
using Humanizer;
using Testing.Datasets.HomeAutomation.Contracts.Events;

namespace Testing.Datasets.HomeAutomation.Model;

public abstract record Device {
    public required string     Id               { get; init; }
    public required string     FriendlyName     { get; init; }
    public required Room       Room             { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }

    public DeviceInfo CreateDeviceInfo(int? batteryLevel = null) =>
        new() {
            Id               = Id,
            Name             = FriendlyName,
            RoomId           = Room.RoomId,
            DeviceType       = DeviceType,
            IsBatteryPowered = IsBatteryPowered,
            BatteryLevel     = IsBatteryPowered ? batteryLevel : null
        };
}
```

**Step 2: Convert all 8 subtypes to sealed records**

Strip mutable state (battery levels, methods). Each becomes a one-liner. The mutable device state will live in
`DeviceState` (Task 7).

`Thermostat.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record Thermostat : Device;
```

`SmartLight.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record SmartLight : Device;
```

`DoorLock.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record DoorLock : Device;
```

`MotionSensor.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record MotionSensor : Device;
```

`SmokeDetector.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record SmokeDetector : Device;
```

`HumiditySensor.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record HumiditySensor : Device;
```

`WindowSensor.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record WindowSensor : Device;
```

`WaterSensor.cs`:

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public sealed record WaterSensor : Device;
```

**Step 3: Build**

```sh
dotnet build Kurrent.Client.slnx
```

Expected: Build errors in Fakers and DataSet (constructor calls, mutable property access). These are fixed in Task 3.

---

### Task 3: Update Fakers for record types

**Files:**

- Modify: `HomeAutomation/Fakers/DeviceFaker.cs`
- Modify: `HomeAutomation/Fakers/TemperatureReadingFaker.cs`
- Modify: `HomeAutomation/Fakers/HumidityReadingFaker.cs`
- Modify: `HomeAutomation/Fakers/MotionDetectedFaker.cs`
- Modify: `HomeAutomation/Fakers/LightStateChangedFaker.cs`
- Modify: `HomeAutomation/Fakers/DoorStateChangedFaker.cs`
- Modify: `HomeAutomation/Fakers/WindowStateChangedFaker.cs`
- Modify: `HomeAutomation/Fakers/SmokeDetectedFaker.cs`
- Modify: `HomeAutomation/Fakers/WaterLeakDetectedFaker.cs`

**Step 1: Update DeviceFaker**

Device subtypes no longer have constructors. Use object initializers with `required` properties. The `FriendlyName`
computation that was in the Device constructor moves here.

```csharp
using Bogus;
using Humanizer;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class DeviceFaker : Faker<Device> {
    public DeviceFaker(Room room, DeviceType? deviceType = null, string? homeId = null) {
        CustomInstantiator(f => {
                var actualDeviceType = deviceType ?? SelectWeightedDeviceType(f, room.Type);
                var deviceId         = GenerateDeviceUrn(homeId ?? f.Random.AlphaNumeric(6).ToLower(), actualDeviceType, f);
                var friendlyName     = $"{room.Name} {actualDeviceType.Humanize()}";
                var isBattery        = IsBatteryPowered(actualDeviceType);

                return actualDeviceType switch {
                    DeviceType.Thermostat     => new Thermostat     { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.MotionSensor   => new MotionSensor   { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.SmartLight     => new SmartLight     { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.DoorLock       => new DoorLock       { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.SmokeDetector  => new SmokeDetector  { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.HumiditySensor => new HumiditySensor { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.WindowSensor   => new WindowSensor   { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    DeviceType.WaterSensor    => new WaterSensor    { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery },
                    _                         => new Thermostat     { Id = deviceId, FriendlyName = friendlyName, Room = room, DeviceType = actualDeviceType, IsBatteryPowered = isBattery }
                };
            }
        );
    }

    static bool IsBatteryPowered(DeviceType type) =>
        type is DeviceType.Thermostat or DeviceType.SmokeDetector or DeviceType.HumiditySensor
            or DeviceType.WindowSensor or DeviceType.WaterSensor;

    // ... keep existing GenerateDeviceUrn, SelectWeightedDeviceType, GetSuitableDeviceTypes, WeightedDevice unchanged
}
```

**Step 2: Update event Fakers**

Each event faker accepted a specific Device subtype and mutated its state. Now they accept the subtype but generate
random values without mutation. The `Device` parameter is used only for `CreateDeviceInfo`.

`TemperatureReadingFaker.cs`: Remove `device.UpdateTemperature()` and `device.DepleteBattery()`. Generate a random
temperature directly.

```csharp
public class TemperatureReadingFaker : Faker<TemperatureReading> {
    public TemperatureReadingFaker(Thermostat device, long timestamp) {
        RuleFor(e => e.EventId, f => f.Random.Guid())
            .RuleFor(e => e.Device, f => device.CreateDeviceInfo(
                device.IsBatteryPowered ? f.Random.Number(5, 100) : null))
            .RuleFor(e => e.Temperature, f => Math.Round(f.Random.Double(15.0, 35.0), 1))
            .RuleFor(e => e.Unit, TemperatureUnit.Celsius)
            .RuleFor(e => e.Timestamp, timestamp);
    }
}
```

Apply the same pattern to all other event fakers: remove device mutation calls, generate random values, pass random
battery level to `CreateDeviceInfo` for battery-powered devices. Preserve the constructor signature (specific subtype).

For each faker, the key change is: remove any `device.Something()` mutation calls and `device.SomeProperty` reads of
mutable state. Replace with `f.Random` calls for the values.

**Step 3: Build**

```sh
dotnet build Kurrent.Client.slnx
```

Expected: `Build succeeded`.

---

### Task 4: Update DataSet and FakerExtensions

**Files:**

- Modify: `HomeAutomation/HomeAutomationDataSet.cs`
- Modify: `HomeAutomation/FakerExtensions.cs`

**Step 1: Switch DataSet to CustomDataSet base**

Change `HomeAutomationDataSet` to extend `CustomDataSet<HomeAutomationDataSet>`. Override `Initialize()` to no-op (no
BSON data file). Use primary constructor. Keep `Default` singleton. Keep all existing method signatures.

```csharp
using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation;

public class HomeAutomationDataSet : CustomDataSet<HomeAutomationDataSet> {
    public static readonly HomeAutomationDataSet Default = new();

    public HomeAutomationDataSet(Faker? faker = null) : base(faker ?? new Faker()) { }

    protected override void Initialize() { }

    // ... keep all existing methods (Home, Homes, Devices, Events) with bodies unchanged
    // except: remove the private `Faker Faker { get; }` property (now inherited from CustomDataSet)
}
```

Key change: the private `Faker Faker { get; }` property and the two constructors are replaced by the single constructor
calling `base(faker)`. The `Faker` property is inherited from `CustomDataSet<T>` as `protected`.

**Step 2: Update FakerExtensions to C# 14 extension syntax**

Replace the extension method with C# 14 `extension` block, matching the ArcadeRacer pattern.

```csharp
using Bogus;

namespace Testing.Datasets.HomeAutomation;

public static class FakerExtensions {
    extension(Faker faker) {
        public HomeAutomationDataSet HomeAutomation() =>
            ContextHelper.GetOrSet(faker, f => new HomeAutomationDataSet(f));
    }
}
```

**Step 3: Build**

```sh
dotnet build Kurrent.Client.slnx
```

Expected: `Build succeeded`. If the `Faker` property access fails because `CustomDataSet.Faker` is `protected` and
DataSet methods use it directly, verify that `HomeAutomationDataSet` methods can access `Faker` (they can, since they
are in the same class which inherits it).

---

### Task 5: Build verify and commit model evolution

**Step 1: Full solution build**

```sh
dotnet build Kurrent.Client.slnx
```

Expected: `Build succeeded` with zero errors. Fix any remaining issues before proceeding.

**Step 2: Commit model evolution**

```sh
git add test/Kurrent.Testing.Datasets/HomeAutomation/
git commit -m "refactor(home-automation): evolve model types to immutable records

SmartHome, Room become records with init properties.
Device becomes abstract record with sealed subtypes.
Fakers updated for object initializer construction.
DataSet switched to CustomDataSet base.
FakerExtensions switched to C# 14 extension syntax."
```

---

### Task 6: Add commands

**Files:**

- Modify: `HomeAutomation/Contracts/HomeAutomationCommands.cs`

**Step 1: Write ISmartHomeCommand and all 15 command records**

The file already exists with an empty namespace. Populate it with the marker interface and all command records. Each
command carries a `DeviceId` string to identify the target device. Some commands carry additional parameters (e.g.,
`RecordTemperature` carries `Temperature` and `Unit`).

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Contracts.Commands;

public interface ISmartHomeCommand {
    string DeviceId { get; }
}

public record RecordTemperature : ISmartHomeCommand {
    public required string          DeviceId    { get; init; }
    public required double          Temperature { get; init; }
    public required TemperatureUnit Unit        { get; init; }
}

public record RecordHumidity : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required double Humidity { get; init; }
}

public record RecordMotion : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required double Confidence { get; init; }
    public required int    ZoneId     { get; init; }
}

public record RecordDoorOpen : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordDoorClose : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordDoorLock : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordDoorUnlock : ISmartHomeCommand {
    public required string DeviceId { get; init; }
    public required string UserId   { get; init; }
}

public record RecordWindowOpen : ISmartHomeCommand {
    public required string DeviceId       { get; init; }
    public required int    OpenPercentage { get; init; }
}

public record RecordWindowClose : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}

public record RecordLightOn : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required int    Brightness { get; init; }
    public required string Color      { get; init; }
}

public record RecordLightOff : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}

public record RecordSmokeDetected : ISmartHomeCommand {
    public required string DeviceId   { get; init; }
    public required int    SmokeLevel { get; init; }
}

public record RecordSmokeCleared : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}

public record RecordWaterLeakDetected : ISmartHomeCommand {
    public required string DeviceId      { get; init; }
    public required int    MoistureLevel { get; init; }
}

public record RecordWaterLeakResolved : ISmartHomeCommand {
    public required string DeviceId { get; init; }
}
```

**Step 2: Build and commit**

```sh
dotnet build Kurrent.Client.slnx
git add test/Kurrent.Testing.Datasets/HomeAutomation/Contracts/HomeAutomationCommands.cs
git commit -m "feat(home-automation): add ISmartHomeCommand and 15 command records"
```

---

### Task 7: Add DeviceState and SmartHomeState

**Files:**

- Create: `HomeAutomation/Model/DeviceState.cs`
- Create: `HomeAutomation/Model/SmartHomeState.cs`

**Step 1: Create DeviceState**

Mutable class tracking per-device runtime state. Each field corresponds to what `Apply` updates.

```csharp
namespace Testing.Datasets.HomeAutomation.Model;

public class DeviceState {
    public required string     DeviceId         { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }

    // Battery
    public int BatteryLevel { get; set; } = 100;

    // Thermostat
    public double CurrentTemperature { get; set; } = 22.0;

    // Light
    public bool   LightIsOn     { get; set; }
    public int    Brightness    { get; set; }
    public string LightColor    { get; set; } = "#FFFFFF";

    // Door
    public bool       DoorIsOpen   { get; set; }
    public LockStatus DoorLock     { get; set; } = LockStatus.Locked;

    // Window
    public bool IsWindowOpen    { get; set; }
    public int  OpenPercentage  { get; set; }

    // Humidity
    public double CurrentHumidity { get; set; } = 45.0;

    // Smoke
    public bool SmokeAlarmActive { get; set; }

    // Water
    public bool WaterLeakDetected { get; set; }

    public void DrainBattery(int amount = 1) {
        if (IsBatteryPowered)
            BatteryLevel = Math.Max(0, BatteryLevel - amount);
    }
}
```

**Step 2: Create SmartHomeState**

Event-sourced entity. `Execute` produces events from commands using current state. `Apply` pattern-matches the event
to update state. Constructor initializes `DeviceState` entries from the `SmartHome` devices list.

```csharp
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
```

**Step 3: Build and commit**

```sh
dotnet build Kurrent.Client.slnx
git add test/Kurrent.Testing.Datasets/HomeAutomation/Model/DeviceState.cs \
        test/Kurrent.Testing.Datasets/HomeAutomation/Model/SmartHomeState.cs
git commit -m "feat(home-automation): add SmartHomeState entity with Execute/Apply"
```

---

### Task 8: Add SeasonProfile

**Files:**

- Create: `HomeAutomation/Simulation/SeasonProfile.cs`

**Step 1: Create SeasonProfile**

Static configuration records for Summer and Winter.

```csharp
namespace Testing.Datasets.HomeAutomation.Simulation;

public record SeasonProfile {
    public required string Name                 { get; init; }
    public required double MinOutdoorTemp       { get; init; }
    public required double MaxOutdoorTemp       { get; init; }
    public required double BaseHumidity         { get; init; }
    public required int    DaylightStartHour    { get; init; }
    public required int    DaylightEndHour      { get; init; }
    public required double WindowOpenProbability { get; init; }

    public static readonly SeasonProfile Summer = new() {
        Name                 = "Summer",
        MinOutdoorTemp       = 20.0,
        MaxOutdoorTemp       = 35.0,
        BaseHumidity         = 55.0,
        DaylightStartHour    = 6,
        DaylightEndHour      = 21,
        WindowOpenProbability = 0.3
    };

    public static readonly SeasonProfile Winter = new() {
        Name                 = "Winter",
        MinOutdoorTemp       = -5.0,
        MaxOutdoorTemp       = 10.0,
        BaseHumidity         = 35.0,
        DaylightStartHour    = 8,
        DaylightEndHour      = 16,
        WindowOpenProbability = 0.02
    };
}
```

**Step 2: Build and commit**

```sh
dotnet build Kurrent.Client.slnx
git add test/Kurrent.Testing.Datasets/HomeAutomation/Simulation/SeasonProfile.cs
git commit -m "feat(home-automation): add SeasonProfile records for Summer and Winter"
```

---

### Task 9: Build simulation engine

**Files:**

- Create: `HomeAutomation/Simulation/HomeAutomationSimulation.cs`

This file contains three types: `HomeSimulator` (static entry point), `HomeAutomationSimulationBuilder`, and
`HomeAutomationSimulation` (the `IAsyncEnumerable`). Follow the ArcadeRacer pattern in
`ArcadeRacer/ArcadeRaceSimulation.cs`.

**Step 1: Create the file with all three types**

The simulation tick logic selects commands probabilistically. Time-of-day periods:

- **Daytime** (`DaylightStartHour` to `DaylightEndHour`): motion likely (0.4), lights off (0.1 turn off, 0.05 turn on),
  windows may open (season probability), temperature/humidity readings (0.6)
- **Evening** (`DaylightEndHour` to `DaylightEndHour + 3`): lights on (0.5), doors lock (0.2), motion decreases (0.15)
- **Night** (remaining hours): minimal activity, temperature/humidity readings only (0.3), rare safety events (0.005)
- **Always**: safety events at very low probability (smoke 0.002, water leak 0.001)

```csharp
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
```

**Step 2: Build and commit**

```sh
dotnet build Kurrent.Client.slnx
git add test/Kurrent.Testing.Datasets/HomeAutomation/Simulation/HomeAutomationSimulation.cs
git commit -m "feat(home-automation): add simulation engine with builder and tick loop

Tick-based event generation with time-of-day probabilistic
command selection, seasonal profiles, and battery drain."
```

---

### Task 10: Write model and DataSet tests

**Files:**

- Create: `HomeAutomation/Tests/ModelTests.cs`
- Create: `HomeAutomation/Tests/DataSetTests.cs`

**Step 1: Write model tests**

Test immutability, record equality, device subtype pattern matching.

```csharp
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Tests;

[Category("HomeAutomation")]
public class ModelTests {
    static Room MakeRoom() => new() { RoomId = Guid.NewGuid(), Name = "Test Room", Type = RoomType.LivingRoom };

    [Test]
    public async Task room_is_record_with_value_equality() {
        var id   = Guid.NewGuid();
        var a    = new Room { RoomId = id, Name = "Room", Type = RoomType.Kitchen };
        var b    = new Room { RoomId = id, Name = "Room", Type = RoomType.Kitchen };
        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task device_subtypes_support_pattern_matching() {
        var room   = MakeRoom();
        Device dev = new Thermostat {
            Id = "t1", FriendlyName = "Thermo", Room = room,
            DeviceType = DeviceType.Thermostat, IsBatteryPowered = true
        };

        var matched = dev switch {
            Thermostat     => "thermostat",
            SmartLight     => "light",
            DoorLock       => "door",
            MotionSensor   => "motion",
            SmokeDetector  => "smoke",
            HumiditySensor => "humidity",
            WindowSensor   => "window",
            WaterSensor    => "water",
            _              => "unknown"
        };

        await Assert.That(matched).IsEqualTo("thermostat");
    }

    [Test]
    public async Task device_creates_device_info_with_battery() {
        var room = MakeRoom();
        var dev  = new Thermostat {
            Id = "t1", FriendlyName = "Thermo", Room = room,
            DeviceType = DeviceType.Thermostat, IsBatteryPowered = true
        };
        var info = dev.CreateDeviceInfo(85);

        await Assert.That(info.Id).IsEqualTo("t1");
        await Assert.That(info.BatteryLevel).IsEqualTo(85);
        await Assert.That(info.IsBatteryPowered).IsTrue();
    }

    [Test]
    public async Task device_creates_device_info_without_battery_for_mains_powered() {
        var room = MakeRoom();
        var dev  = new SmartLight {
            Id = "l1", FriendlyName = "Light", Room = room,
            DeviceType = DeviceType.SmartLight, IsBatteryPowered = false
        };
        var info = dev.CreateDeviceInfo(50);

        await Assert.That(info.BatteryLevel).IsNull();
    }

    [Test]
    public async Task smart_home_is_record_with_value_equality() {
        var room = MakeRoom();
        var a = new SmartHome { Id = "h1", Name = "Home", Rooms = [room], Devices = [], CreatedAt = DateTime.UnixEpoch };
        var b = new SmartHome { Id = "h1", Name = "Home", Rooms = [room], Devices = [], CreatedAt = DateTime.UnixEpoch };

        await Assert.That(a).IsEqualTo(b);
    }
}
```

**Step 2: Write DataSet tests**

```csharp
using Bogus;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Tests;

[Category("HomeAutomation")]
public class DataSetTests {
    [Test]
    public async Task default_singleton_is_not_null() {
        await Assert.That(HomeAutomationDataSet.Default).IsNotNull();
    }

    [Test]
    public async Task home_has_rooms_and_devices() {
        var home = HomeAutomationDataSet.Default.Home();
        await Assert.That(home.Rooms.Count).IsGreaterThan(0);
        await Assert.That(home.Devices.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task homes_produces_multiple() {
        var homes = HomeAutomationDataSet.Default.Homes(3);
        await Assert.That(homes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task events_produces_requested_count() {
        var events = HomeAutomationDataSet.Default.Events(20);
        await Assert.That(events.Count).IsEqualTo(20);
    }

    [Test]
    public async Task events_reference_valid_device_types() {
        var home   = HomeAutomationDataSet.Default.Home();
        var events = HomeAutomationDataSet.Default.Events(home, 10);

        foreach (var evt in events)
            await Assert.That(evt.Device.DeviceType).IsNotEqualTo(DeviceType.Unspecified);
    }

    [Test]
    public async Task devices_match_subtype_to_device_type_enum() {
        var home = HomeAutomationDataSet.Default.Home();

        foreach (var device in home.Devices) {
            var matches = device switch {
                Thermostat     => device.DeviceType == DeviceType.Thermostat,
                SmartLight     => device.DeviceType == DeviceType.SmartLight,
                DoorLock       => device.DeviceType == DeviceType.DoorLock,
                MotionSensor   => device.DeviceType == DeviceType.MotionSensor,
                SmokeDetector  => device.DeviceType == DeviceType.SmokeDetector,
                HumiditySensor => device.DeviceType == DeviceType.HumiditySensor,
                WindowSensor   => device.DeviceType == DeviceType.WindowSensor,
                WaterSensor    => device.DeviceType == DeviceType.WaterSensor,
                _              => false
            };
            await Assert.That(matches).IsTrue();
        }
    }

    [Test]
    public async Task faker_extension_returns_cached_instance() {
        var faker = new Faker();
        var ds1   = faker.HomeAutomation();
        var ds2   = faker.HomeAutomation();
        await Assert.That(ds1).IsSameReferenceAs(ds2);
    }

    [Test]
    public async Task infinite_events_stream_produces_events() {
        var events = HomeAutomationDataSet.Default.Events().Take(50).ToList();
        await Assert.That(events.Count).IsEqualTo(50);
    }
}
```

**Step 3: Run tests and commit**

```sh
dotnet run --project test/Kurrent.Testing.Datasets/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=HomeAutomation]"
```

Expected: All tests pass.

```sh
git add test/Kurrent.Testing.Datasets/HomeAutomation/Tests/ModelTests.cs \
        test/Kurrent.Testing.Datasets/HomeAutomation/Tests/DataSetTests.cs
git commit -m "test(home-automation): add model and DataSet tests"
```

---

### Task 11: Write state tests

**Files:**

- Create: `HomeAutomation/Tests/SmartHomeStateTests.cs`

**Step 1: Write state tests**

Test Execute/Apply for each command type, battery drain, unknown device rejection.

```csharp
using Testing.Datasets.HomeAutomation.Contracts.Commands;
using Testing.Datasets.HomeAutomation.Contracts.Events;
using Testing.Datasets.HomeAutomation.Model;

namespace Testing.Datasets.HomeAutomation.Tests;

[Category("HomeAutomation")]
public class SmartHomeStateTests {
    static readonly Room TestRoom = new() { RoomId = Guid.NewGuid(), Name = "Test", Type = RoomType.LivingRoom };

    static SmartHomeState CreateState(params Device[] devices) {
        var home = new SmartHome {
            Id = "h1", Name = "Test Home", Rooms = [TestRoom],
            Devices = [..devices], CreatedAt = DateTime.UtcNow
        };
        var state = new SmartHomeState();
        state.Initialize(home, 1000L);
        return state;
    }

    static Thermostat MakeThermo() => new() {
        Id = "t1", FriendlyName = "Thermo", Room = TestRoom,
        DeviceType = DeviceType.Thermostat, IsBatteryPowered = true
    };

    static SmartLight MakeLight() => new() {
        Id = "l1", FriendlyName = "Light", Room = TestRoom,
        DeviceType = DeviceType.SmartLight, IsBatteryPowered = false
    };

    #region Execute produces correct events

    [Test]
    public async Task execute_record_temperature_produces_temperature_reading() {
        var state = CreateState(MakeThermo());
        var cmd   = new RecordTemperature { DeviceId = "t1", Temperature = 22.5, Unit = TemperatureUnit.Celsius };
        var evt   = state.Execute(cmd, 2000L);

        await Assert.That(evt).IsTypeOf<TemperatureReading>();
        var tr = (TemperatureReading)evt;
        await Assert.That(tr.Temperature).IsEqualTo(22.5);
        await Assert.That(tr.Device.Id).IsEqualTo("t1");
    }

    [Test]
    public async Task execute_record_light_on_produces_light_state_changed() {
        var state = CreateState(MakeLight());
        var cmd   = new RecordLightOn { DeviceId = "l1", Brightness = 80, Color = "#FFD700" };
        var evt   = state.Execute(cmd, 2000L);

        await Assert.That(evt).IsTypeOf<LightStateChanged>();
        var lsc = (LightStateChanged)evt;
        await Assert.That(lsc.IsOn).IsTrue();
        await Assert.That(lsc.Brightness).IsEqualTo(80);
    }

    #endregion

    #region Apply updates state

    [Test]
    public async Task apply_temperature_reading_updates_current_temperature() {
        var state = CreateState(MakeThermo());
        var cmd   = new RecordTemperature { DeviceId = "t1", Temperature = 25.0, Unit = TemperatureUnit.Celsius };
        var evt   = state.Execute(cmd, 2000L);
        state.Apply(evt);

        await Assert.That(state.Devices["t1"].CurrentTemperature).IsEqualTo(25.0);
    }

    [Test]
    public async Task apply_light_on_updates_light_state() {
        var state = CreateState(MakeLight());
        var cmd   = new RecordLightOn { DeviceId = "l1", Brightness = 70, Color = "#FFF" };
        state.Apply(state.Execute(cmd, 2000L));

        await Assert.That(state.Devices["l1"].LightIsOn).IsTrue();
        await Assert.That(state.Devices["l1"].Brightness).IsEqualTo(70);
    }

    [Test]
    public async Task apply_updates_current_time() {
        var state = CreateState(MakeThermo());
        var cmd   = new RecordTemperature { DeviceId = "t1", Temperature = 20.0, Unit = TemperatureUnit.Celsius };
        state.Apply(state.Execute(cmd, 5000L));

        await Assert.That(state.CurrentTime).IsEqualTo(5000L);
    }

    #endregion

    #region Battery drain

    [Test]
    public async Task battery_drains_on_battery_powered_device() {
        var state = CreateState(MakeThermo());
        var initial = state.Devices["t1"].BatteryLevel;

        var cmd = new RecordTemperature { DeviceId = "t1", Temperature = 20.0, Unit = TemperatureUnit.Celsius };
        state.Apply(state.Execute(cmd, 2000L));

        await Assert.That(state.Devices["t1"].BatteryLevel).IsLessThan(initial);
    }

    [Test]
    public async Task battery_stable_on_mains_powered_device() {
        var state = CreateState(MakeLight());
        var initial = state.Devices["l1"].BatteryLevel;

        var cmd = new RecordLightOn { DeviceId = "l1", Brightness = 50, Color = "#FFF" };
        state.Apply(state.Execute(cmd, 2000L));

        await Assert.That(state.Devices["l1"].BatteryLevel).IsEqualTo(initial);
    }

    #endregion

    #region Error handling

    [Test]
    public async Task execute_rejects_unknown_device_id() {
        var state = CreateState(MakeThermo());
        var cmd   = new RecordTemperature { DeviceId = "unknown", Temperature = 20.0, Unit = TemperatureUnit.Celsius };

        await Assert.That(() => state.Execute(cmd, 2000L)).Throws<InvalidOperationException>();
    }

    #endregion
}
```

**Step 2: Run tests and commit**

```sh
dotnet run --project test/Kurrent.Testing.Datasets/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=HomeAutomation]"
```

Expected: All tests pass.

```sh
git add test/Kurrent.Testing.Datasets/HomeAutomation/Tests/SmartHomeStateTests.cs
git commit -m "test(home-automation): add SmartHomeState Execute/Apply tests"
```

---

### Task 12: Write simulation tests

**Files:**

- Create: `HomeAutomation/Tests/SimulationTests.cs`

**Step 1: Write simulation tests**

Follow the ArcadeRacer simulation test patterns. Test basic operation, max events, cancellation, season effects,
time-of-day effects, increasing timestamps, battery depletion.

```csharp
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
```

**Step 2: Run tests and commit**

```sh
dotnet run --project test/Kurrent.Testing.Datasets/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=HomeAutomation]"
```

Expected: All tests pass.

```sh
git add test/Kurrent.Testing.Datasets/HomeAutomation/Tests/SimulationTests.cs
git commit -m "test(home-automation): add simulation tests for streaming, seasons, battery"
```

---

### Task 13: Final verification

**Step 1: Full solution build**

```sh
dotnet build Kurrent.Client.slnx
```

Expected: `Build succeeded` with zero errors across all projects including consumer projects
(`Kurrent.Client.Integration.Tests`, `Kurrent.Processors.Integration.Tests`, `Kurrent.Client.Benchmarks`).

**Step 2: Run all HomeAutomation tests**

```sh
dotnet run --project test/Kurrent.Testing.Datasets/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=HomeAutomation]"
```

Expected: All tests pass.

**Step 3: Run ArcadeRacer tests (regression check)**

```sh
dotnet run --project test/Kurrent.Testing.Datasets/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net10.0 \
  --treenode-filter "/*/*/*/*[Category=ArcadeRacer]"
```

Expected: All ArcadeRacer tests still pass.

**Step 4: Update README**

Update `test/Kurrent.Testing.Datasets/README.md` to set `HomeAutomation` simulation column to `Yes` and add the
simulation usage example.

**Step 5: Commit**

```sh
git add test/Kurrent.Testing.Datasets/README.md
git commit -m "docs: update README with HomeAutomation simulation status"
```
