# HomeAutomationV2 Design

## Contents

- [Context](#context)
- [Goals](#goals)
- [Non-goals](#non-goals)
- [File structure](#file-structure)
- [Model layer](#model-layer)
  - [Value objects](#value-objects)
  - [Enums](#enums)
  - [Domain types](#domain-types)
  - [DeviceInfo snapshot](#deviceinfo-snapshot)
- [Events](#events)
  - [Interface](#interface)
  - [Event catalog](#event-catalog)
- [Simulation profiles](#simulation-profiles)
- [Simulation engine](#simulation-engine)
  - [API surface](#api-surface)
  - [Internal state machine](#internal-state-machine)
  - [Simulation loop](#simulation-loop)
  - [Realism details](#realism-details)
- [Data generation](#data-generation)
  - [Dataset](#dataset)
  - [Faker extension](#faker-extension)

## Context

The existing `Sample/HomeAutomation/` domain model has structural issues identified during code review:

- Mutable state with public setters; no invariant protection
- Abstract base class inheritance hierarchy (`Device` with 7 subclasses)
- Inconsistent `required` modifiers on the event interface
- Primitive obsession (raw `Guid`, `string`, `int` for identities and constrained values)
- Side effects in property initializers (`DateTime.UtcNow`)
- CRUD-style events (`DoorStateChanged`) instead of domain-intent events (`DoorOpened`, `DoorClosed`)
- Magic numbers scattered through device classes

HomeAutomationV2 resolves all issues except fat events (issue 7); `DeviceInfo` remains embedded in every event per user
preference.

## Goals

- Apply DDD and modern C# best practices to the IoT home automation domain
- Full value objects (`readonly record struct`) for identities and constrained values
- Immutable model types; no public setters on domain records
- Composition over inheritance; no abstract `Device` base class
- Domain-intent events split from CRUD-style combined events
- Occupant-driven simulation that produces realistic, correlated events
- `IAsyncEnumerable<IHomeEvent>` output with optional wall-clock pacing
- Bogus `DataSet` for generating `SmartHome` instances following the Personas pattern
- Coexist alongside V1 in `Sample/HomeAutomationV2/`; V1 consumers untouched

## Non-goals

- JSON serialization converters for value objects (consumers can add if needed)
- Replacing V1 consumers (benchmarks, integration tests)
- Individual `Faker<T>` classes per type; the dataset handles all generation

## File structure

```
test/Kurrent.Testing/Sample/HomeAutomationV2/
├── HomeAutomationModel.cs        — Value objects, enums, Room, Device, SmartHome, DeviceInfo
├── HomeAutomationEvents.cs       — IHomeEvent interface, 16 split event records
├── HomeAutomationProfiles.cs     — OccupantRoutine, WeatherProfile, HouseholdProfile
├── HomeAutomationSimulation.cs   — HomeSimulator, HomeSimulationBuilder, HomeSimulation
├── HomeAutomationDataSet.cs      — DataSet for generating SmartHome instances and profiles
├── HomeAutomationExtensions.cs   — Faker extension: faker.HomeAutomationV2()
```

## Model layer

### Value objects

All `readonly record struct` with constructor validation.

| Type          | Wraps    | Constraint         |
|---------------|----------|--------------------|
| `DeviceId`    | `string` | Non-empty          |
| `RoomId`      | `Guid`   | Non-empty          |
| `HomeId`      | `string` | Non-empty          |
| `Percentage`  | `int`    | 0 to 100 inclusive |
| `SensorLevel` | `int`    | 0 to 100 inclusive |
| `HexColor`    | `string` | `#RRGGBB` format   |

`Temperature` stays as `double` + `TemperatureUnit` on event properties directly. `Timestamp` stays as `long`
(Unix ms); consistent with all existing event infrastructure.

### Enums

All enums include `Unspecified = 0` as the first value.

Carried from V1: `RoomType`, `DeviceType`, `TemperatureUnit`, `AlarmStatus`, `LockStatus`, `WeatherCondition`,
`ComfortLevel`.

New: `Season` (Spring, Summer, Autumn, Winter).

### Domain types

```csharp
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
```

No abstract base class. No inheritance hierarchy. `Device` is a flat record; behavioral differences are handled by
the simulation engine based on `DeviceType`.

### DeviceInfo snapshot

Fat snapshot embedded in every event (issue 7 preserved by design choice).

```csharp
public record DeviceInfo {
    public required DeviceId    Id               { get; init; }
    public required string      Name             { get; init; }
    public required RoomId      RoomId           { get; init; }
    public required DeviceType  DeviceType       { get; init; }
    public required bool        IsBatteryPowered { get; init; }
    public          Percentage? BatteryLevel     { get; init; }
}
```

## Events

### Interface

```csharp
public interface IHomeEvent {
    Guid       EventId   { get; init; }
    DeviceInfo Device    { get; init; }
    long       Timestamp { get; init; }
}
```

All implementing records mark every property as `required`.

### Event catalog

16 events split by domain intent.

| V1 Source            | V2 Events                                                | Key Properties                      |
|----------------------|----------------------------------------------------------|-------------------------------------|
| `TemperatureReading` | `TemperatureReading`                                        | Temperature, Unit                  |
| `MotionDetected`     | `MotionDetected`                                            | Confidence, ZoneId                 |
| `HumidityReading`    | `HumidityReading`                                           | Humidity, ComfortLevel             |
| `DoorStateChanged`   | `DoorOpened`, `DoorClosed`, `DoorLocked`, `DoorUnlocked`    | UserId                             |
| `WindowStateChanged` | `WindowOpened`, `WindowClosed`                              | OpenPercentage, WeatherCondition   |
| `LightStateChanged`  | `LightTurnedOn`, `LightTurnedOff`, `LightAdjusted`         | Brightness, Color, PowerConsumption |
| `SmokeDetected`      | `SmokeAlarmTriggered`, `SmokeAlarmCleared`                  | SmokeLevel, AlarmStatus            |
| `WaterLeakDetected`  | `WaterLeakDetected`, `WaterLeakResolved`                    | MoistureLevel, AlertSent           |

Value objects used on event properties: `Percentage` for battery, brightness, moisture, smoke level; `HexColor` for
light color.

## Simulation profiles

```csharp
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
}

public record HouseholdProfile {
    public required SmartHome                      Home      { get; init; }
    public required IReadOnlyList<OccupantRoutine> Occupants { get; init; }
    public required WeatherProfile                 Weather   { get; init; }
}
```

`WeatherProfile` uses `Season` to derive baseline outdoor temperature and humidity. `BaseTemp` is the daily average;
`TempSwing` is the peak-to-trough diurnal range. Indoor temperatures lag outdoor with damping.

## Simulation engine

### API surface

```csharp
public static class HomeSimulator {
    public static HomeSimulationBuilder Create() => new();
}

// Usage
await foreach (var evt in HomeSimulator.Create()
    .ForHome(home)
    .WithOccupants(occupants)
    .WithWeather(weatherProfile)
    .WithPacing(TimeSpan.FromMilliseconds(50))
    .MaxEvents(1000)
    .StartAt(DateTimeOffset.Now)
    .Build()) { ... }
```

Convenience overloads: `.WithOccupants(int count)` auto-generates routines; `.WithSeason(Season)` auto-generates a
weather profile.

### Internal state machine

The simulation tracks mutable state internally; none is exposed on model types.

- **Virtual clock**: advances in variable ticks (1 to 10 minutes simulation time per step)
- **Occupant states**: `(RoomId CurrentRoom, OccupantActivity Activity)` per occupant
- **Device states**: mutable dictionary keyed by `DeviceId` with type-specific state (current temp, battery level,
  isOn, brightness, lock status, moisture level, etc.)

### Simulation loop

Each tick executes in order:

1. Advance clock by tick interval
2. Weather drift: slow temperature and humidity changes; rare condition changes
3. Occupant movement:
   - Schedule-driven: wake to kitchen, leave to away, return to living room, sleep to bedroom
   - Stochastic: random room transitions while home (kitchen to living room, bathroom visits)
   - Weekend: later wake time, no leave/return cycle, more room variety
4. Device reactions:
   - Motion sensors: emit `MotionDetected` when occupant enters room
   - Lights: turn on when occupant enters dark room (time-based); off when room empties
   - Doors: open/close on departure/arrival; lock on departure
   - Temperature: drift toward outdoor temp (damped); thermostat readings periodic
   - Humidity: drift based on weather plus bathroom occupancy spikes
   - Windows: occasional open/close based on weather (nice weather triggers open)
5. Autonomous device behaviors:
   - Battery drain: slow (0.01 to 0.05% per tick for battery devices)
   - Rare smoke alarm: very low probability; higher during cooking hours
   - Rare water leak: very low probability
6. Yield all events generated this tick
7. If pacing configured: `await Task.Delay(pacing, cancellationToken)`

### Realism details

- Motion confidence correlates with simulated distance from sensor (randomized 0.6 to 0.99)
- Light brightness correlates with time of day (dimmer in evening)
- Temperature follows sinusoidal diurnal curve (peak around 14:00, trough around 04:00)
- Cooking hours (11:30 to 13:00, 18:00 to 20:00) increase kitchen activity and smoke probability
- Bathroom humidity spikes last 15 to 30 minutes after occupant visit
- Weekend schedules differ: later wake, no commute, more room transitions

## Data generation

### Dataset

Follows the Personas pattern. No BSON; fully generative.

```csharp
public class HomeAutomationDataSet : DataSet {
    public SmartHome SmartHome(int? rooms = null, int? devicesPerRoom = null);
    public Room Room(RoomType? type = null);
    public Device Device(RoomId roomId, DeviceType? type = null);
    public OccupantRoutine Occupant(RoomId bedroom, bool? worksFromHome = null);
    public WeatherProfile Weather(Season? season = null);
    public HouseholdProfile Household(SmartHome? home = null, int? occupants = null);
}
```

`SmartHome()` generates a realistic home with 4 to 8 rooms and 1 to 3 devices per room. Device types match room
types: thermostats in living areas, smoke detectors everywhere, water sensors in kitchen/bathroom/basement.

### Faker extension

C# 14 `extension` syntax following the Personas pattern.

```csharp
public static class HomeAutomationExtensions {
    extension(Faker faker) {
        public HomeAutomationDataSet HomeAutomationV2() =>
            ContextHelper.GetOrSet(faker, f => new HomeAutomationDataSet(f));
    }
}
```
