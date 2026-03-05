# HomeAutomation Simulation Design

## Contents

- [Goal](#goal)
- [Architecture](#architecture)
- [Model evolution](#model-evolution)
- [Contracts](#contracts)
- [SmartHomeState entity](#smarthomestate-entity)
- [Seasonal profiles](#seasonal-profiles)
- [Simulation engine](#simulation-engine)
- [DataSet changes](#dataset-changes)
- [File structure](#file-structure)
- [Backwards compatibility](#backwards-compatibility)
- [Test plan](#test-plan)

## Goal

Build an event-sourced IoT simulation engine for the HomeAutomation dataset that produces realistic
`IHomeAutomationEvent` streams via `IAsyncEnumerable`. Supports infinite streaming or capped by max event count.
Command-event pipeline architecture with `SmartHomeState` as the central entity.

## Architecture

Each simulation tick:

1. Advance clock by 1 to 10 random minutes
2. Evaluate time-of-day + season + device states to select commands
3. For each `Record*` command: `SmartHomeState.Execute(command)` returns an `IHomeAutomationEvent`
4. `SmartHomeState.Apply(event)` updates internal state (battery drain, device toggles, readings)
5. Yield event to consumer

Commands are 1:1 with events. `Execute` produces events based on current state. `Apply` replays events onto state.
If the consumer needs a batch, the simulation issues multiple commands in a single tick.

## Model evolution

Existing mutable classes become immutable records. Property names and types are preserved for consumer compatibility.

`SmartHome`, `Room` become immutable records with `{ get; init; }` properties.

`Device` becomes an abstract record. Subtypes (`Thermostat`, `SmartLight`, `DoorLock`, `MotionSensor`,
`SmokeDetector`, `HumiditySensor`, `WindowSensor`, `WaterSensor`) become sealed records inheriting from `Device`.
Pattern matching is preserved.

```csharp
public abstract record Device {
    public required string     Id               { get; init; }
    public required string     FriendlyName     { get; init; }
    public required Room       Room             { get; init; }
    public required DeviceType DeviceType       { get; init; }
    public required bool       IsBatteryPowered { get; init; }
}

public sealed record Thermostat : Device;
public sealed record SmartLight : Device;
public sealed record DoorLock   : Device;
// ... remaining subtypes
```

## Contracts

### Events (existing, preserved)

`IHomeAutomationEvent` interface and all 8 event records stay as-is: `TemperatureReading`, `HumidityReading`,
`MotionDetected`, `DoorStateChanged`, `WindowStateChanged`, `LightStateChanged`, `SmokeDetected`,
`WaterLeakDetected`. `DeviceInfo` record stays as-is.

### Commands (new)

`ISmartHomeCommand` marker interface. Each command is an immutable record carrying the device ID and parameters
needed to produce its corresponding event.

| Command                   | Produces             |
|---------------------------|----------------------|
| `RecordTemperature`       | `TemperatureReading` |
| `RecordHumidity`          | `HumidityReading`    |
| `RecordMotion`            | `MotionDetected`     |
| `RecordDoorOpen`          | `DoorStateChanged`   |
| `RecordDoorClose`         | `DoorStateChanged`   |
| `RecordDoorLock`          | `DoorStateChanged`   |
| `RecordDoorUnlock`        | `DoorStateChanged`   |
| `RecordWindowOpen`        | `WindowStateChanged` |
| `RecordWindowClose`       | `WindowStateChanged` |
| `RecordLightOn`           | `LightStateChanged`  |
| `RecordLightOff`          | `LightStateChanged`  |
| `RecordSmokeDetected`     | `SmokeDetected`      |
| `RecordSmokeCleared`      | `SmokeDetected`      |
| `RecordWaterLeakDetected` | `WaterLeakDetected`  |
| `RecordWaterLeakResolved` | `WaterLeakDetected`  |

## SmartHomeState entity

Event-sourced state class. Private property sets. Mutated only via `Apply`.

```csharp
public class SmartHomeState {
    public SmartHome Home { get; private set; }
    public long CurrentTime { get; private set; }
    public IReadOnlyDictionary<string, DeviceState> Devices { get; }

    public IHomeAutomationEvent Execute(ISmartHomeCommand command);
    public void Apply(IHomeAutomationEvent evt);
}
```

`DeviceState` tracks per-device mutable state:

- Battery level (decremented in `Apply` for battery-powered devices)
- Thermostat: current temperature
- Light: on/off, brightness, color
- Door: open/closed, lock status
- Window: open/closed, open percentage
- Humidity sensor: current humidity
- Smoke detector: alarm active
- Water sensor: leak detected

`Execute` reads `DeviceState` and `SeasonProfile` to produce realistic values. `Apply` pattern-matches the event
type and updates the corresponding `DeviceState` fields.

## Seasonal profiles

Static configuration records that feed into simulation tick decisions.

```csharp
public record SeasonProfile {
    public required string Name                  { get; init; }
    public required double MinOutdoorTemp        { get; init; }
    public required double MaxOutdoorTemp        { get; init; }
    public required double BaseHumidity          { get; init; }
    public required int    DaylightStartHour     { get; init; }
    public required int    DaylightEndHour       { get; init; }
    public required double WindowOpenProbability { get; init; }

    public static readonly SeasonProfile Summer = new() { ... };
    public static readonly SeasonProfile Winter = new() { ... };
}
```

Summer: high temps (20 to 35C), long daylight (6 to 21), windows open often (0.3).
Winter: low temps (-5 to 10C), short daylight (8 to 16), windows rarely open (0.02).

## Simulation engine

### Public API

```csharp
public static class HomeSimulator {
    public static HomeAutomationSimulationBuilder NewSimulation() => new();
}

public class HomeAutomationSimulationBuilder {
    public HomeAutomationSimulationBuilder ForHome(SmartHome home);
    public HomeAutomationSimulationBuilder WithSeason(SeasonProfile profile);
    public HomeAutomationSimulationBuilder WithPacing(TimeSpan pacing);
    public HomeAutomationSimulationBuilder MaxEvents(int count);
    public HomeAutomationSimulationBuilder StartAt(DateTimeOffset time);
    public HomeAutomationSimulation Create();
}

public class HomeAutomationSimulation : IAsyncEnumerable<IHomeAutomationEvent> { }
```

### Tick logic

Each tick evaluates time-of-day against the season profile to select commands probabilistically:

- **Daytime** (daylight hours): motion likely, lights off, windows may open, periodic sensor readings
- **Evening** (post-daylight): lights on with warm glow, doors lock, motion decreases
- **Night**: minimal activity, rare events only
- **Always**: temperature and humidity readings every few ticks; rare safety events (smoke, water leak)

Battery drain: `Apply` decrements battery on battery-powered devices each time they produce an event. The decreasing
`DeviceInfo.BatteryLevel` on subsequent events reflects drain naturally.

### Infinite mode

When `MaxEvents` is 0 or unset, the simulation runs until the cancellation token fires. The tick loop has no
termination condition beyond cancellation.

## DataSet changes

`HomeAutomationDataSet` switches from plain `DataSet` to `CustomDataSet<HomeAutomationDataSet>`. The `Default`
singleton, `Home()`, `Homes()`, `Events()`, and `Devices()` method signatures are preserved. `FakerExtensions`
switches to C# 14 `extension` syntax. Individual `Faker<T>` classes in `Fakers/` are updated for the new model
types.

## File structure

```
HomeAutomation/
  Model/              SmartHome, Room, Device subtypes, enums, SmartHomeState, DeviceState
  Contracts/          IHomeAutomationEvent, events, DeviceInfo, ISmartHomeCommand, Record* commands
  Simulation/         HomeAutomationSimulation, builder, SeasonProfile
  Fakers/             Consolidated device/room fakers
  Tests/              Model, DataSet, State, Simulation tests
  HomeAutomationDataSet.cs
  FakerExtensions.cs
```

## Backwards compatibility

### Preserved

- `HomeAutomationDataSet.Default` singleton
- `.Home()`, `.Homes()`, `.Events()`, `.Devices()` method signatures
- `SmartHome`, `Room`, `Device` property names and types
- `IHomeAutomationEvent` interface and all event records
- `DeviceInfo` record
- Device subtypes for pattern matching

### Changed

- `SmartHome`, `Room` become immutable records (`init` instead of `set`)
- `Device` becomes abstract record with sealed subtypes
- `FakerExtensions` uses C# 14 extension syntax
- DataSet base class becomes `CustomDataSet<HomeAutomationDataSet>`

### Added

- `ISmartHomeCommand` and 15 command records
- `SmartHomeState` entity with `Execute`/`Apply`
- `HomeAutomationSimulation` with builder and `IAsyncEnumerable`
- `SeasonProfile` records
- Comprehensive test suite

### Verification

Full solution build after changes. Run integration tests (`Kurrent.Client.Integration.Tests`,
`Kurrent.Processors.Integration.Tests`) and benchmarks (`Kurrent.Client.Benchmarks`) to confirm no regressions.

## Test plan

### Model tests

- Value object validation (immutability, required properties)
- Device subtype pattern matching
- Record equality

### State tests

- `Execute` produces correct event for each command
- `Apply` updates correct `DeviceState` fields
- Battery drain per event on battery-powered devices
- Battery stable on mains-powered devices
- State rejects commands for unknown device IDs

### DataSet tests

- `Home()` produces valid home with rooms and devices
- `Homes()` produces multiple homes
- `Events()` produces events referencing valid devices
- `Devices()` generates devices with correct types per room
- Device subtypes match `DeviceType` enum
- `Default` singleton works

### Simulation tests

- Produces events (basic operation)
- Respects `MaxEvents` cap
- Runs until cancelled when infinite
- Runs fast without pacing
- Season affects temperature readings
- Time-of-day affects light events
- Events have increasing timestamps
- Events reference valid devices from configured home
- Battery depletes over time
- Cancellation handled gracefully
