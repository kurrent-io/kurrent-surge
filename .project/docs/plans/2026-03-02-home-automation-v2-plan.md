# HomeAutomationV2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a best-practices IoT domain model with occupant-driven simulation engine that produces realistic,
correlated smart home events via `IAsyncEnumerable<IHomeEvent>`.

**Architecture:** Immutable model types with `readonly record struct` value objects. Flat `Device` record (no
inheritance). Internal mutable state for simulation only. Occupant movement drives device events through a tick-based
loop. Builder pattern configures simulation. Bogus `DataSet` generates `SmartHome` instances.

**Tech Stack:** C# 14, TUnit, Bogus, `IAsyncEnumerable<T>`

**Design doc:** [2026-03-02-home-automation-v2-design.md](2026-03-02-home-automation-v2-design.md)

**Reference patterns:**

- `test/Kurrent.Testing/Sample/FormulaOne/FormulaOneSimulation.cs` — `IAsyncEnumerable` simulation, builder, internal
  `Race` state class, `EmitEvent` delegate
- `test/Kurrent.Testing/Sample/FormulaOne/Tests/SimulationTests.cs` — TUnit test patterns for simulations
- `test/Kurrent.Testing/Bogus/Datasets/Personas/PersonasDataSet.cs` — `CustomDataSet<T>` base, `ContextHelper`
- `test/Kurrent.Testing/Bogus/Datasets/Personas/PersonasExtensions.cs` — C# 14 `extension` syntax for Faker

**Skills:** @dotnet-skills:csharp-coding-standards, @domain-driven-design, @bogus

---

## Contents

- [Task 1: Model — value objects, enums, domain types](#task-1-model--value-objects-enums-domain-types)
- [Task 2: Events — IHomeEvent and 16 event records](#task-2-events--ihomeevent-and-16-event-records)
- [Task 3: Profiles — occupant routines and weather](#task-3-profiles--occupant-routines-and-weather)
- [Task 4: DataSet and Faker extension](#task-4-dataset-and-faker-extension)
- [Task 5: Model and DataSet tests](#task-5-model-and-dataset-tests)
- [Task 6: Simulation — internal state types and constants](#task-6-simulation--internal-state-types-and-constants)
- [Task 7: Simulation — builder and skeleton](#task-7-simulation--builder-and-skeleton)
- [Task 8: Simulation — occupant movement engine](#task-8-simulation--occupant-movement-engine)
- [Task 9: Simulation — device behaviors and weather](#task-9-simulation--device-behaviors-and-weather)
- [Task 10: Simulation — main loop wiring](#task-10-simulation--main-loop-wiring)
- [Task 11: Simulation tests](#task-11-simulation-tests)
- [Task 12: Final verification](#task-12-final-verification)

---

### Task 1: Model — value objects, enums, domain types

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationModel.cs`

**Step 1: Create the file with all value objects, enums, and domain types**

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable NotAccessedPositionalProperty.Global

namespace Kurrent.Testing.Sample.HomeAutomationV2;

#region Value Objects

public readonly record struct DeviceId {
    public string Value { get; }

    public DeviceId(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

public readonly record struct RoomId {
    public Guid Value { get; }

    public RoomId(Guid value) {
        if (value == Guid.Empty)
            throw new ArgumentException("RoomId cannot be empty.", nameof(value));
        Value = value;
    }

    public static RoomId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

public readonly record struct HomeId {
    public string Value { get; }

    public HomeId(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

public readonly record struct Percentage {
    public int Value { get; }

    public Percentage(int value) {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
        Value = value;
    }

    public override string ToString() => $"{Value}%";
}

public readonly record struct SensorLevel {
    public int Value { get; }

    public SensorLevel(int value) {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
        Value = value;
    }

    public override string ToString() => $"{Value}/100";
}

public readonly record struct HexColor {
    public string Value { get; }

    public HexColor(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != 7 || value[0] != '#')
            throw new ArgumentException($"HexColor must be #RRGGBB format, got: {value}", nameof(value));

        for (var i = 1; i < 7; i++) {
            if (!char.IsAsciiHexDigit(value[i]))
                throw new ArgumentException($"HexColor must be #RRGGBB format, got: {value}", nameof(value));
        }

        Value = value.ToUpperInvariant();
    }

    public override string ToString() => Value;

    public static readonly HexColor White   = new("#FFFFFF");
    public static readonly HexColor WarmGlow = new("#FFD699");
    public static readonly HexColor Daylight = new("#F5F5DC");
}

#endregion

#region Enums

public enum RoomType {
    Unspecified = 0,
    LivingRoom,
    Kitchen,
    Bedroom,
    Bathroom,
    Office,
    Garage,
    Hallway,
    DiningRoom,
    Basement,
    Utility,
    Entrance
}

public enum DeviceType {
    Unspecified = 0,
    Thermostat,
    MotionSensor,
    SmartLight,
    DoorLock,
    SmokeDetector,
    HumiditySensor,
    WindowSensor,
    WaterSensor
}

public enum TemperatureUnit {
    Unspecified = 0,
    Celsius,
    Fahrenheit
}

public enum AlarmStatus {
    Unspecified = 0,
    Normal,
    Active,
    Testing,
    Maintenance
}

public enum LockStatus {
    Unspecified = 0,
    Locked,
    Unlocked,
    Jammed
}

public enum WeatherCondition {
    Unspecified = 0,
    Sunny,
    Cloudy,
    Rainy,
    Windy,
    Stormy
}

public enum ComfortLevel {
    Unspecified = 0,
    Low,
    Optimal,
    High
}

public enum Season {
    Unspecified = 0,
    Spring,
    Summer,
    Autumn,
    Winter
}

#endregion

#region Domain Types

public record Room {
    public required RoomId   Id   { get; init; }
    public required string   Name { get; init; }
    public required RoomType Type { get; init; }
}

public record Device {
    public required DeviceId   Id               { get; init; }
    public required string     Name             { get; init; }
    public required RoomId     RoomId           { get; init; }
    public required DeviceType Type             { get; init; }
    public required bool       IsBatteryPowered { get; init; }
}

public record SmartHome {
    public required HomeId                Id      { get; init; }
    public required string                Name    { get; init; }
    public required IReadOnlyList<Room>   Rooms   { get; init; }
    public required IReadOnlyList<Device> Devices { get; init; }
}

public record DeviceInfo {
    public required DeviceId    Id               { get; init; }
    public required string      Name             { get; init; }
    public required RoomId      RoomId           { get; init; }
    public required DeviceType  DeviceType       { get; init; }
    public required bool        IsBatteryPowered { get; init; }
    public          Percentage? BatteryLevel     { get; init; }
}

#endregion
```

**Step 2: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

Expected: `Build succeeded.`

**Step 3: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationModel.cs
git commit -m "feat(testing): add HomeAutomationV2 model with value objects and domain types"
```

---

### Task 2: Events — IHomeEvent and 16 event records

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationEvents.cs`

**Step 1: Create events file with interface and all 16 records**

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable UnusedParameter.Local
// ReSharper disable ClassNeverInstantiated.Global

namespace Kurrent.Testing.Sample.HomeAutomationV2;

public interface IHomeEvent {
    Guid       EventId   { get; init; }
    DeviceInfo Device    { get; init; }
    long       Timestamp { get; init; }
}

#region Temperature and Environment

public record TemperatureReading : IHomeEvent {
    public required Guid            EventId     { get; init; }
    public required DeviceInfo      Device      { get; init; }
    public required double          Temperature { get; init; }
    public required TemperatureUnit Unit        { get; init; }
    public required long            Timestamp   { get; init; }
}

public record HumidityReading : IHomeEvent {
    public required Guid         EventId      { get; init; }
    public required DeviceInfo   Device       { get; init; }
    public required double       Humidity     { get; init; }
    public required ComfortLevel ComfortLevel { get; init; }
    public required long         Timestamp    { get; init; }
}

#endregion

#region Motion

public record MotionDetected : IHomeEvent {
    public required Guid       EventId    { get; init; }
    public required DeviceInfo Device     { get; init; }
    public required double     Confidence { get; init; }
    public required int        ZoneId     { get; init; }
    public required long       Timestamp  { get; init; }
}

#endregion

#region Door

public record DoorOpened : IHomeEvent {
    public required Guid       EventId   { get; init; }
    public required DeviceInfo Device    { get; init; }
    public required string     UserId    { get; init; }
    public required long       Timestamp { get; init; }
}

public record DoorClosed : IHomeEvent {
    public required Guid       EventId   { get; init; }
    public required DeviceInfo Device    { get; init; }
    public required string     UserId    { get; init; }
    public required long       Timestamp { get; init; }
}

public record DoorLocked : IHomeEvent {
    public required Guid       EventId   { get; init; }
    public required DeviceInfo Device    { get; init; }
    public required string     UserId    { get; init; }
    public required long       Timestamp { get; init; }
}

public record DoorUnlocked : IHomeEvent {
    public required Guid       EventId   { get; init; }
    public required DeviceInfo Device    { get; init; }
    public required string     UserId    { get; init; }
    public required long       Timestamp { get; init; }
}

#endregion

#region Window

public record WindowOpened : IHomeEvent {
    public required Guid             EventId          { get; init; }
    public required DeviceInfo       Device           { get; init; }
    public required Percentage       OpenPercentage   { get; init; }
    public required WeatherCondition WeatherCondition { get; init; }
    public required long             Timestamp        { get; init; }
}

public record WindowClosed : IHomeEvent {
    public required Guid             EventId          { get; init; }
    public required DeviceInfo       Device           { get; init; }
    public required WeatherCondition WeatherCondition { get; init; }
    public required long             Timestamp        { get; init; }
}

#endregion

#region Lighting

public record LightTurnedOn : IHomeEvent {
    public required Guid       EventId          { get; init; }
    public required DeviceInfo Device           { get; init; }
    public required Percentage Brightness       { get; init; }
    public required HexColor   Color            { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}

public record LightTurnedOff : IHomeEvent {
    public required Guid       EventId   { get; init; }
    public required DeviceInfo Device    { get; init; }
    public required long       Timestamp { get; init; }
}

public record LightAdjusted : IHomeEvent {
    public required Guid       EventId          { get; init; }
    public required DeviceInfo Device           { get; init; }
    public required Percentage Brightness       { get; init; }
    public required HexColor   Color            { get; init; }
    public required double     PowerConsumption { get; init; }
    public required long       Timestamp        { get; init; }
}

#endregion

#region Safety

public record SmokeAlarmTriggered : IHomeEvent {
    public required Guid        EventId     { get; init; }
    public required DeviceInfo  Device      { get; init; }
    public required Percentage  SmokeLevel  { get; init; }
    public required AlarmStatus AlarmStatus { get; init; }
    public required long        Timestamp   { get; init; }
}

public record SmokeAlarmCleared : IHomeEvent {
    public required Guid        EventId     { get; init; }
    public required DeviceInfo  Device      { get; init; }
    public required AlarmStatus AlarmStatus { get; init; }
    public required long        Timestamp   { get; init; }
}

public record WaterLeakDetected : IHomeEvent {
    public required Guid       EventId       { get; init; }
    public required DeviceInfo Device        { get; init; }
    public required Percentage MoistureLevel { get; init; }
    public required bool       AlertSent     { get; init; }
    public required long       Timestamp     { get; init; }
}

public record WaterLeakResolved : IHomeEvent {
    public required Guid       EventId   { get; init; }
    public required DeviceInfo Device    { get; init; }
    public required long       Timestamp { get; init; }
}

#endregion
```

**Step 2: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

Expected: `Build succeeded.`

**Step 3: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationEvents.cs
git commit -m "feat(testing): add HomeAutomationV2 events with domain-intent split"
```

---

### Task 3: Profiles — occupant routines and weather

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationProfiles.cs`

**Step 1: Create profiles file**

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Testing.Sample.HomeAutomationV2;

public record OccupantRoutine {
    public required string   Name          { get; init; }
    public required TimeOnly WakeTime      { get; init; }
    public required TimeOnly LeaveTime     { get; init; }
    public required TimeOnly ReturnTime    { get; init; }
    public required TimeOnly SleepTime     { get; init; }
    public required bool     WorksFromHome { get; init; }
    public required RoomId   Bedroom       { get; init; }
}

public record WeatherProfile {
    public required Season           Season       { get; init; }
    public required double           BaseTemp     { get; init; }
    public required double           TempSwing    { get; init; }
    public required WeatherCondition Condition    { get; init; }
    public required double           BaseHumidity { get; init; }

    public static WeatherProfile For(Season season) => season switch {
        Season.Spring => new WeatherProfile {
            Season       = Season.Spring,
            BaseTemp     = 15.0,
            TempSwing    = 10.0,
            Condition    = WeatherCondition.Cloudy,
            BaseHumidity = 55.0
        },
        Season.Summer => new WeatherProfile {
            Season       = Season.Summer,
            BaseTemp     = 28.0,
            TempSwing    = 12.0,
            Condition    = WeatherCondition.Sunny,
            BaseHumidity = 40.0
        },
        Season.Autumn => new WeatherProfile {
            Season       = Season.Autumn,
            BaseTemp     = 12.0,
            TempSwing    = 8.0,
            Condition    = WeatherCondition.Rainy,
            BaseHumidity = 65.0
        },
        Season.Winter => new WeatherProfile {
            Season       = Season.Winter,
            BaseTemp     = 2.0,
            TempSwing    = 6.0,
            Condition    = WeatherCondition.Cloudy,
            BaseHumidity = 70.0
        },
        _ => For(Season.Summer)
    };
}

public record HouseholdProfile {
    public required SmartHome                      Home      { get; init; }
    public required IReadOnlyList<OccupantRoutine> Occupants { get; init; }
    public required WeatherProfile                 Weather   { get; init; }
}
```

**Step 2: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 3: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationProfiles.cs
git commit -m "feat(testing): add HomeAutomationV2 profiles for occupants and weather"
```

---

### Task 4: DataSet and Faker extension

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationDataSet.cs`
- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationExtensions.cs`

**Step 1: Create the DataSet**

Reference: `test/Kurrent.Testing/Sample/HomeAutomation/HomeAutomationDataSet.cs` for the existing V1 pattern.
Reference: `test/Kurrent.Testing/Sample/FormulaOne/FormulaOneDataSet.cs` for the `Default` singleton and constructor
patterns.

The DataSet must:

- Extend Bogus `DataSet` (NOT `CustomDataSet<T>` since there is no BSON data)
- Accept both `string locale` and `Faker faker` constructors
- Expose a `public static readonly HomeAutomationDataSet Default` singleton
- Generate realistic homes with device types matched to room types

Room-to-device mapping logic (which device types make sense in which rooms):

| Room Type    | Typical Devices                                          |
|--------------|----------------------------------------------------------|
| LivingRoom   | Thermostat, MotionSensor, SmartLight                     |
| Kitchen      | SmokeDetector, MotionSensor, SmartLight, WaterSensor     |
| Bedroom      | Thermostat, SmartLight, WindowSensor                     |
| Bathroom     | HumiditySensor, WaterSensor, SmartLight                  |
| Office       | Thermostat, SmartLight, MotionSensor                     |
| Garage       | MotionSensor, DoorLock, SmartLight                       |
| Hallway      | MotionSensor, SmartLight                                 |
| DiningRoom   | SmartLight, WindowSensor                                 |
| Basement     | WaterSensor, SmokeDetector, MotionSensor                 |
| Utility      | WaterSensor, SmokeDetector                               |
| Entrance     | DoorLock, MotionSensor, SmartLight                       |

Battery-powered device types: `Thermostat`, `SmokeDetector`, `HumiditySensor`, `WindowSensor`, `WaterSensor`.
Mains-powered: `MotionSensor`, `SmartLight`, `DoorLock`.

Home name generation: use patterns like `"{LastName} Family Home"`, `"{StreetName} House"`,
`"{City} Apartment {N}"`.

`SmartHome()` default: 4 to 8 rooms, 1 to 3 devices per room. Always include at least one `Entrance` room and one
`Kitchen`.

`Occupant()` generation: realistic schedule jitter around base times:

- Wake: 06:00 to 08:00
- Leave: 07:30 to 09:30
- Return: 16:30 to 19:00
- Sleep: 21:00 to 23:30
- WorksFromHome: 30% probability

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Humanizer;

namespace Kurrent.Testing.Sample.HomeAutomationV2;

public class HomeAutomationDataSet : DataSet {
    public static readonly HomeAutomationDataSet Default = new();

    public HomeAutomationDataSet(string locale = "en") : base(locale) =>
        Faker = new Faker(locale);

    public HomeAutomationDataSet(Faker faker) : base(faker.Locale) =>
        Faker = faker;

    Faker Faker { get; }

    // ... implement SmartHome(), Room(), Device(), Occupant(), Weather(), Household()
    // Follow the room-to-device mapping table above
    // Follow the patterns established in FormulaOneDataSet
}
```

Write the complete implementation following the mapping table and generation rules above. Key methods:

- `SmartHome(int? rooms = null, int? devicesPerRoom = null)` — generates a complete home
- `Room(RoomType? type = null)` — generates a single room with random type if not specified
- `Device(RoomId roomId, string roomName, DeviceType type)` — generates a device for a room
- `Occupant(RoomId bedroom, bool? worksFromHome = null)` — generates an occupant routine
- `Weather(Season? season = null)` — generates a weather profile
- `Household(SmartHome? home = null, int? occupants = null, Season? season = null)` — generates a full profile

Device naming: use `$"{roomName} {deviceType.Humanize()}"` (requires `using Humanizer`).

Device ID format: `$"{deviceType.ToString().ToLower()}-{Faker.Random.AlphaNumeric(6)}"`.

**Step 2: Create the Faker extension**

Follow the Personas pattern using C# 14 `extension` syntax. Reference:
`test/Kurrent.Testing/Bogus/Datasets/Personas/PersonasExtensions.cs`.

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testing.Bogus.Datasets;

namespace Kurrent.Testing.Sample.HomeAutomationV2;

public static class HomeAutomationExtensions {
    extension(Faker faker) {
        public HomeAutomationDataSet HomeAutomationV2() =>
            ContextHelper.GetOrSet(faker, f => new HomeAutomationDataSet(f));
    }
}
```

**Step 3: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 4: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationDataSet.cs \
        test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationExtensions.cs
git commit -m "feat(testing): add HomeAutomationV2 dataset and Faker extension"
```

---

### Task 5: Model and DataSet tests

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/ModelTests.cs`
- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/DataSetTests.cs`

Reference: `test/Kurrent.Testing/Sample/FormulaOne/Tests/DataSetTests.cs` for test structure.
Reference: `test/Kurrent.Testing/Sample/FormulaOne/Tests/SimulationTests.cs` for TUnit patterns.

TUnit test patterns used in this codebase:

- `[Test]` attribute on `async Task` methods
- `[Category("...")]` on the class
- `Assert.That(x).IsNotEmpty()`, `Assert.That(x).IsEqualTo(y)`, `Assert.That(x).HasCount().EqualTo(n)`
- `TestCaseGenerator<T>` for parameterized tests
- `#region` blocks to group related tests
- Test method names: `snake_case`

**Step 1: Write ModelTests**

Test value object validation:

- `percentage_rejects_negative` — `new Percentage(-1)` throws `ArgumentOutOfRangeException`
- `percentage_rejects_over_100` — `new Percentage(101)` throws
- `percentage_accepts_boundary_values` — `new Percentage(0)` and `new Percentage(100)` succeed
- `device_id_rejects_empty` — `new DeviceId("")` throws `ArgumentException`
- `room_id_rejects_empty_guid` — `new RoomId(Guid.Empty)` throws
- `hex_color_rejects_invalid_format` — `new HexColor("red")` throws
- `hex_color_normalizes_to_uppercase` — `new HexColor("#aabbcc")` produces `Value == "#AABBCC"`

**Step 2: Write DataSetTests**

- `smart_home_has_rooms_and_devices` — `Default.SmartHome()` produces a home with rooms and devices
- `smart_home_devices_reference_valid_rooms` — every device `RoomId` exists in `home.Rooms`
- `smart_home_includes_entrance` — at least one room is `RoomType.Entrance`
- `smart_home_includes_kitchen` — at least one room is `RoomType.Kitchen`
- `smart_home_respects_room_count` — `SmartHome(rooms: 5)` produces exactly 5 rooms
- `occupant_schedule_is_chronological` — `WakeTime < LeaveTime < ReturnTime < SleepTime`
- `household_generates_complete_profile` — `Household()` has home, occupants, and weather
- `weather_profile_for_all_seasons` — parameterized test with `Season` enum values

**Step 3: Run tests**

```sh
dotnet run --project test/Kurrent.Testing/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net8.0 \
  --treenode-filter "/*/*/ModelTests/*"
```

```sh
dotnet run --project test/Kurrent.Testing/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net8.0 \
  --treenode-filter "/*/*/DataSetTests/*"
```

Wait. Check if `Kurrent.Testing` is a test-runnable project. If it is not (class library only), the tests must go in
`test/Kurrent.Client.Tests/` instead. Check the `.csproj` for `<OutputType>Exe</OutputType>` or TUnit test runner
references. The FormulaOne tests live inside `Kurrent.Testing` so it likely IS runnable.

**Step 4: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/
git commit -m "test(testing): add HomeAutomationV2 model and dataset tests"
```

---

### Task 6: Simulation — internal state types and constants

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs`

This task creates the file with internal types only. The public API and simulation logic come in later tasks.

**Step 1: Create simulation file with internal state types**

```csharp
// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Kurrent.Testing.Sample.HomeAutomationV2;

delegate void EmitEvent(IHomeEvent e);

#region Constants

static class SimConstants {
    // Tick duration range in simulation minutes
    public const int MinTickMinutes = 1;
    public const int MaxTickMinutes = 10;
    public const long TickMillisPerMinute = 60_000L;

    // Battery drain per tick (percentage points * 100 for precision)
    public const double BatteryDrainPerTick = 0.03;
    public const int MinBatteryLevel = 5;

    // Temperature
    public const double IndoorDampingFactor  = 0.1;
    public const double ThermostatSetpoint   = 21.0;
    public const double ThermostatWeight     = 0.7;
    public const double OutdoorWeight        = 0.3;
    public const double TempReadingJitter    = 0.3;

    // Humidity
    public const double BaseIndoorHumidity    = 45.0;
    public const double BathroomHumiditySpike = 25.0;
    public const int    HumiditySpikeDuration = 20; // minutes

    // Probabilities (per tick)
    public const float SmokeAlarmBaseProb       = 0.0005f;
    public const float SmokeAlarmCookingProb    = 0.005f;
    public const float WaterLeakBaseProb        = 0.0003f;
    public const float WindowToggleNiceWeather  = 0.01f;
    public const float RandomRoomTransitionProb = 0.15f;

    // Light
    public const int    EveningDimBrightness = 60;
    public const int    DaytimeBrightness    = 100;
    public const double LightPowerPerBrightness = 0.6; // watts per brightness unit

    // Cooking hours (hour of day)
    public static readonly (int Start, int End)[] CookingWindows = [(11, 13), (18, 20)];
}

#endregion

#region Internal State

internal enum OccupantActivity {
    Sleeping,
    Active,
    Cooking,
    Showering,
    Away
}

internal class OccupantState {
    public required OccupantRoutine Routine  { get; init; }
    public          RoomId?         CurrentRoom { get; set; }
    public          OccupantActivity Activity   { get; set; } = OccupantActivity.Sleeping;
}

internal class DeviceRuntimeState {
    public required DeviceType Type             { get; init; }
    public required DeviceId   Id               { get; init; }
    public required RoomId     RoomId           { get; init; }
    public required string     Name             { get; init; }
    public required bool       IsBatteryPowered { get; init; }

    // Common
    public double BatteryLevel { get; set; } = 95.0;

    // Thermostat
    public double CurrentTemp { get; set; } = SimConstants.ThermostatSetpoint;

    // Light
    public bool   LightIsOn       { get; set; }
    public int    LightBrightness { get; set; }
    public string LightColor      { get; set; } = HexColor.White.Value;

    // Door
    public bool       DoorIsOpen     { get; set; }
    public LockStatus DoorLockStatus { get; set; } = LockStatus.Locked;

    // Window
    public bool WindowIsOpen       { get; set; }
    public int  WindowOpenPercent  { get; set; }

    // Humidity
    public double CurrentHumidity    { get; set; } = SimConstants.BaseIndoorHumidity;
    public int    HumiditySpikeTicks { get; set; }

    // Smoke
    public bool SmokeIsAlarming { get; set; }
    public int  SmokeAlarmTicks { get; set; }

    // Water
    public bool WaterDetected    { get; set; }
    public int  WaterMoisture    { get; set; }
    public int  WaterLeakTicks   { get; set; }

    public DeviceInfo ToDeviceInfo() => new() {
        Id               = Id,
        Name             = Name,
        RoomId           = RoomId,
        DeviceType       = Type,
        IsBatteryPowered = IsBatteryPowered,
        BatteryLevel     = IsBatteryPowered ? new Percentage((int)Math.Round(BatteryLevel)) : null
    };
}

internal class HouseholdState {
    public required SmartHome                               Home      { get; init; }
    public required WeatherProfile                          Weather   { get; init; }
    public required OccupantState[]                         Occupants { get; init; }
    public required Dictionary<DeviceId, DeviceRuntimeState> Devices   { get; init; }

    public long     CurrentTime    { get; set; }
    public double   OutdoorTemp    { get; set; }
    public double   OutdoorHumidity { get; set; }
    public bool     IsWeekend      { get; set; }

    public TimeOnly TimeOfDay => TimeOnly.FromTimeSpan(
        DateTimeOffset.FromUnixTimeMilliseconds(CurrentTime).TimeOfDay
    );

    public int HourOfDay => TimeOfDay.Hour;
}

#endregion
```

**Step 2: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 3: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs
git commit -m "feat(testing): add HomeAutomationV2 simulation internal state types"
```

---

### Task 7: Simulation — builder and skeleton

**Files:**

- Modify: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs`

**Step 1: Add the public API classes**

Add these ABOVE the internal types (after the `EmitEvent` delegate). Follow the FormulaOne builder pattern from
`FormulaOneSimulation.cs:393-472`.

```csharp
public static class HomeSimulator {
    public static HomeSimulationBuilder Create() => new();
}

public record HomeSimulationOptions {
    public SmartHome?                      Home      { get; init; }
    public IReadOnlyList<OccupantRoutine>? Occupants { get; init; }
    public int                             OccupantCount { get; init; } = 2;
    public WeatherProfile?                 Weather   { get; init; }
    public Season                          Season    { get; init; } = Season.Summer;
    public TimeSpan                        Pacing    { get; init; } = TimeSpan.Zero;
    public int                             MaxEvents { get; init; }
    public DateTimeOffset?                 StartAt   { get; init; }
}

public class HomeSimulationBuilder {
    HomeSimulationOptions _options = new();

    public HomeSimulationBuilder ForHome(SmartHome home) {
        _options = _options with { Home = home };
        return this;
    }

    public HomeSimulationBuilder WithOccupants(IReadOnlyList<OccupantRoutine> occupants) {
        _options = _options with { Occupants = occupants };
        return this;
    }

    public HomeSimulationBuilder WithOccupants(int count) {
        _options = _options with { OccupantCount = count };
        return this;
    }

    public HomeSimulationBuilder WithWeather(WeatherProfile weather) {
        _options = _options with { Weather = weather };
        return this;
    }

    public HomeSimulationBuilder WithSeason(Season season) {
        _options = _options with { Season = season };
        return this;
    }

    public HomeSimulationBuilder WithPacing(TimeSpan pacing) {
        _options = _options with { Pacing = pacing };
        return this;
    }

    public HomeSimulationBuilder MaxEvents(int count) {
        _options = _options with { MaxEvents = count };
        return this;
    }

    public HomeSimulationBuilder StartAt(DateTimeOffset startTime) {
        _options = _options with { StartAt = startTime };
        return this;
    }

    public HomeSimulation Build() {
        var faker   = new Faker();
        var dataset = new HomeAutomationDataSet(faker);

        var home      = _options.Home ?? dataset.SmartHome();
        var weather   = _options.Weather ?? WeatherProfile.For(_options.Season);
        var occupants = _options.Occupants ?? GenerateOccupants(dataset, home, _options.OccupantCount);

        var resolved = _options with { Home = home, Weather = weather, Occupants = occupants };
        return new HomeSimulation(resolved, faker);
    }

    static IReadOnlyList<OccupantRoutine> GenerateOccupants(
        HomeAutomationDataSet dataset, SmartHome home, int count
    ) {
        var bedrooms = home.Rooms.Where(r => r.Type == RoomType.Bedroom).ToList();
        return Enumerable.Range(0, count)
            .Select(i => dataset.Occupant(
                bedrooms.Count > 0 ? bedrooms[i % bedrooms.Count].Id : home.Rooms[0].Id
            ))
            .ToList();
    }
}

public class HomeSimulation : IAsyncEnumerable<IHomeEvent> {
    readonly Faker _faker;

    internal HomeSimulation(HomeSimulationOptions options, Faker faker) {
        Options = options;
        _faker  = faker;
    }

    public HomeSimulationOptions Options { get; }

    public async IAsyncEnumerator<IHomeEvent> GetAsyncEnumerator(CancellationToken ct = default) {
        // Placeholder — wired in Task 10
        yield break;
    }

    async ValueTask Pace(CancellationToken ct) {
        if (Options.Pacing == TimeSpan.Zero) return;

        try { await Task.Delay(Options.Pacing, ct); }
        catch (OperationCanceledException) { /* checked in main loop */ }
    }
}
```

**Step 2: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 3: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs
git commit -m "feat(testing): add HomeAutomationV2 simulation builder and skeleton"
```

---

### Task 8: Simulation — occupant movement engine

**Files:**

- Modify: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs`

**Step 1: Add the internal `Household` class with occupant movement**

Add an `internal class Household` after the public classes, before the internal state types. This follows the
FormulaOne `Race` class pattern: holds mutable state, exposes event-producing methods.

The occupant movement algorithm:

```
For each occupant, determine desired state from schedule + current time:
  time < wake         → Sleeping in bedroom
  wake ≤ time < leave → Active at home (cycle: bedroom → kitchen → bathroom → living room)
  leave ≤ time < return → Away (unless worksFromHome → in office)
  return ≤ time < sleep → Active at home (living room → kitchen → bedroom)
  time ≥ sleep        → Sleeping in bedroom

Weekend override:
  shift wake +1.5h, skip leave/return, stay home all day

When an occupant moves to a new room, emit MotionDetected for motion sensors in that room.
When transitioning to Away, emit DoorOpened → DoorClosed → DoorLocked on entrance doors.
When returning from Away, emit DoorUnlocked → DoorOpened → DoorClosed on entrance doors.
```

Stochastic room transitions: while `Active`, occupants randomly move to adjacent rooms with probability
`RandomRoomTransitionProb` per tick. The `Cooking` activity triggers if the occupant is in the kitchen during
cooking hours. The `Showering` activity triggers once per day in the bathroom.

Implement these methods on `Household`:

- `static Household Initialize(HomeSimulationOptions options, Faker faker)` — builds initial state
- `IHomeEvent[] MoveOccupants()` — schedule-driven + stochastic movement
- Helper: `IHomeEvent[] TransitionOccupant(OccupantState occ, RoomId? targetRoom, OccupantActivity activity)`
- Helper: `IHomeEvent[] EmitDoorEvents(string userId, bool departing)`

All event creation uses `Guid.NewGuid()` for `EventId` and `State.CurrentTime` for `Timestamp`.

**Step 2: Build and verify**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 3: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs
git commit -m "feat(testing): add HomeAutomationV2 occupant movement engine"
```

---

### Task 9: Simulation — device behaviors and weather

**Files:**

- Modify: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs`

**Step 1: Add device reaction methods to `Household`**

These methods check device state and emit events. They run each tick after occupant movement.

Methods to implement on `Household`:

- `IHomeEvent[] ReactLights()` — turn on lights in occupied rooms (if dark outside: `HourOfDay < 7 || HourOfDay > 19`),
  turn off in unoccupied rooms. Brightness based on time: evening = `EveningDimBrightness`, day = `DaytimeBrightness`.
  Color: evening = `WarmGlow`, day = `Daylight`.

- `IHomeEvent[] ReactTemperature()` — emit `TemperatureReading` from thermostats periodically (every 3-5 ticks).
  Temperature follows diurnal curve:
  `outdoor = baseTemp + (swing/2) * sin((hour - 8) * π / 12)`
  Indoor drifts toward: `outdoor * 0.3 + setpoint * 0.7`, with damping factor 0.1 per tick.
  Add jitter: `±TempReadingJitter`.

- `IHomeEvent[] ReactHumidity()` — emit `HumidityReading` from humidity sensors periodically. Base humidity tracks
  outdoor. Bathroom spike: +25% for 20 minutes after `Showering` activity.
  ComfortLevel: `< 30% → Low, 30-60% → Optimal, > 60% → High`.

- `IHomeEvent[] ReactWindows()` — occasional `WindowOpened` in nice weather (`Sunny`/`Cloudy`, daytime, not winter).
  Auto-close after 30-60 min or on weather change.

- `IHomeEvent[] AutonomousBehaviors()` — battery drain on all battery devices. Rare `SmokeAlarmTriggered` (higher
  during cooking hours in kitchen). Rare `WaterLeakDetected` (in kitchen/bathroom/basement). Both auto-resolve after
  5-15 ticks.

**Step 2: Add weather drift method**

- `void UpdateWeather()` — slowly drift `OutdoorTemp` and `OutdoorHumidity` each tick using the diurnal curve.
  Occasional weather condition change (very low probability, ~0.002 per tick).

**Step 3: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 4: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs
git commit -m "feat(testing): add HomeAutomationV2 device behaviors and weather drift"
```

---

### Task 10: Simulation — main loop wiring

**Files:**

- Modify: `test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs`

**Step 1: Wire `GetAsyncEnumerator` to the `Household` tick loop**

Replace the placeholder `yield break` in `HomeSimulation.GetAsyncEnumerator`:

```csharp
public async IAsyncEnumerator<IHomeEvent> GetAsyncEnumerator(CancellationToken ct = default) {
    var household = Household.Initialize(Options, _faker);
    var emitted   = 0;
    var maxEvents = Options.MaxEvents;

    while (!ct.IsCancellationRequested) {
        var events = household.Tick();

        foreach (var e in events) {
            yield return e;
            await Pace(ct);

            if (maxEvents > 0 && ++emitted >= maxEvents)
                yield break;
        }
    }
}
```

The `Household.Tick()` method orchestrates one simulation step:

```csharp
public IHomeEvent[] Tick() {
    var events = new List<IHomeEvent>();

    AdvanceClock();
    UpdateWeather();

    events.AddRange(MoveOccupants());
    events.AddRange(ReactLights());
    events.AddRange(ReactTemperature());
    events.AddRange(ReactHumidity());
    events.AddRange(ReactWindows());
    events.AddRange(AutonomousBehaviors());

    return events.ToArray();
}
```

`AdvanceClock()` advances `State.CurrentTime` by 1 to 10 minutes (randomized). Also updates `TimeOfDay`,
`HourOfDay`, and `IsWeekend` (derived from day-of-week of the timestamp).

**Step 2: Smoke test manually**

```csharp
// Quick verification via a scratch test or console
await foreach (var e in HomeSimulator.Create().MaxEvents(10).Build())
    Console.WriteLine($"{e.GetType().Name}: {e.Timestamp}");
```

**Step 3: Build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 4: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs
git commit -m "feat(testing): wire HomeAutomationV2 simulation main loop"
```

---

### Task 11: Simulation tests

**Files:**

- Create: `test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/SimulationTests.cs`

Reference: `test/Kurrent.Testing/Sample/FormulaOne/Tests/SimulationTests.cs` for test patterns.

**Step 1: Write simulation tests**

Test categories to cover:

**Basic operation:**

- `simulation_generates_events` — `HomeSimulator.Create().MaxEvents(50).Build()` produces events
- `simulation_respects_max_events` — exactly `MaxEvents` events produced (or fewer if sim ends)
- `simulation_without_max_events_runs_until_cancelled` — cancel after 100 events, verify count
- `simulation_without_pacing_runs_fast` — `Stopwatch` confirms < 5 seconds for 200 events
- `simulation_via_static_entry_point` — `HomeSimulator.Create().Build()` produces events

**Configuration:**

- `simulation_respects_season` — `WithSeason(Season.Winter)`, verify temperature readings are cold
- `simulation_respects_home` — `ForHome(home)`, verify device events reference the home's devices
- `simulation_respects_occupant_count` — `WithOccupants(3)`, verify occupant-driven events occur

**Event correctness:**

- `simulation_produces_motion_events` — at least some `MotionDetected` in output
- `simulation_produces_temperature_readings` — at least some `TemperatureReading` in output
- `simulation_produces_light_events` — at least some `LightTurnedOn` or `LightTurnedOff`
- `simulation_produces_door_events` — at least some `DoorOpened` or `DoorClosed`
- `simulation_events_have_increasing_timestamps` — timestamps are monotonically non-decreasing
- `simulation_events_reference_valid_devices` — all `Device.Id` values exist in the configured home

**Behavioral properties:**

- `simulation_temperature_follows_season` — Winter readings average below 10C; Summer above 20C.
  Run 500 events for each season, compute average temperature, assert range.
- `simulation_battery_depletes_over_time` — collect battery levels from `DeviceInfo` across events for the same
  battery-powered device. Later events should have lower battery. (Sample 1000 events, find any battery device that
  appears multiple times, assert last value ≤ first value.)

**Cancellation:**

- `simulation_handles_cancellation_gracefully` — cancel token mid-stream, no exceptions thrown

**Step 2: Run tests**

```sh
dotnet run --project test/Kurrent.Testing/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net8.0 \
  --treenode-filter "/*/*/SimulationTests/*"
```

If `Kurrent.Testing` is not a test runner, use `test/Kurrent.Client.Tests/` and add the tests there instead.

**Step 3: Fix any failures, iterate**

If tests fail, debug and fix the simulation logic. Common issues:

- Empty event arrays (no occupants matched any schedule)
- Timestamp not advancing (clock not wired)
- Battery not depleting (drain too small for event count)
- Temperature not varying (weather not updating)

**Step 4: Commit**

```sh
git add test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/SimulationTests.cs
git commit -m "test(testing): add HomeAutomationV2 simulation tests"
```

---

### Task 12: Final verification

**Step 1: Full build**

```sh
dotnet build test/Kurrent.Testing/Kurrent.Testing.csproj --no-restore -v q
```

**Step 2: Run all HomeAutomationV2 tests**

```sh
dotnet run --project test/Kurrent.Testing/ \
  --disable-logo --no-ansi --no-progress --fail-fast \
  --output Detailed --framework net8.0 \
  --treenode-filter "/*/*/HomeAutomationV2*/*"
```

All tests must pass.

**Step 3: Verify file structure**

```sh
find test/Kurrent.Testing/Sample/HomeAutomationV2 -type f -name '*.cs' | sort
```

Expected:

```
test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationDataSet.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationEvents.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationExtensions.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationModel.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationProfiles.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/HomeAutomationSimulation.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/DataSetTests.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/ModelTests.cs
test/Kurrent.Testing/Sample/HomeAutomationV2/Tests/SimulationTests.cs
```

**Step 4: Review diff for unintended changes**

```sh
git diff --stat HEAD~12..HEAD
```

Verify only HomeAutomationV2 files were touched. No V1 files modified.

**Step 5: Final commit if any cleanup needed**

```sh
git add -A test/Kurrent.Testing/Sample/HomeAutomationV2/
git commit -m "chore(testing): HomeAutomationV2 final cleanup"
```
