# ArcadeRacer Dataset

Bogus-powered test dataset that simulates arcade-style car races, producing a stream of
domain events suitable for testing event sourcing, stream processing, and subscription scenarios.

## Quick start

```csharp
var faker = new Faker();
var ds    = faker.ArcadeRacer();
```

Simulate a race and get the complete logbook:

```csharp
var race = await ds.NewRace()
    .WithCars(12)
    .WithLaps(20)
    .WithDifficulty(CircuitDifficulty.Hard)
    .WithConditions(TimeOfDay.Night, Weather.Rainy)
    .SimulateAsync();

var output = TestContext.Current!.Output;

output.WriteLine($"{race.Winner.Pilot} wins in {race.Winner.CarName} ({race.Winner.CumulativeTime:F3}s)");
output.WriteLine($"Circuit: {race.Circuit.Name} ({race.TotalLaps} laps)");

foreach (var r in race.Results)
    output.WriteLine(r.Dnf
        ? $"  P{r.Position} {r.Pilot,-16} DNF"
        : $"  P{r.Position} {r.Pilot,-16} +{r.GapToLeader:F3}s");

// Full event history is available
var overtakes = race.Events.OfType<OvertakePerformed>().Count();
var incidents = race.Events.OfType<IncidentOccurred>().Count();
```

Stream events live and access the logbook after:

```csharp
var sim = ds.NewRace()
    .WithCars(8)
    .WithLaps(10)
    .Start();

await foreach (var e in sim) {
    // process each event as it happens...
}

// After enumeration, the logbook is ready
var race = sim.Result;
race.Winner.Pilot    // who won
race.Events.Count    // total events produced
```

Endless mode — runs until cancelled:

```csharp
var sim = ds.NewRace()
    .WithEndlessLaps()
    .Start();

using var cts = new CancellationTokenSource();

await foreach (var e in sim.WithCancellation(cts.Token)) {
    // process events...
    if (someCondition) cts.Cancel();
}

// Logbook is available even after cancellation
var race = sim.Result;
```

## Builder options

| Method                                           | Description                                               | Default         |
|--------------------------------------------------|-----------------------------------------------------------|-----------------|
| `WithCars(int count)`                            | Number of cars on the grid                                | Random 8-16     |
| `WithLaps(int laps)`                             | Total laps                                                | Random 5-20     |
| `WithEndlessLaps()`                              | Run indefinitely until cancelled                          | -               |
| `WithDifficulty(CircuitDifficulty difficulty)`   | Circuit difficulty: `Easy`, `Medium`, `Hard`              | Random          |
| `WithConditions(TimeOfDay tod, Weather weather)` | Time of day (`Day`/`Night`) and weather (`Sunny`/`Rainy`) | Random          |
| `WithPacing(TimeSpan pacing)`                    | Delay between yielded events                              | `TimeSpan.Zero` |

## Events

Every event implements `IRaceEvent` with an `EventId` (Guid) and `Timestamp` (Unix ms).

| Event               | When emitted                                 | Key data                                       |
|---------------------|----------------------------------------------|------------------------------------------------|
| `GridFormed`        | Once at setup                                | `Cars[]`, `Circuit`, `Conditions`, `TotalLaps` |
| `RaceStarted`       | Once after grid                              | Establishes the race clock                     |
| `LapCompleted`      | Per car, per lap                             | `CarId`, `Lap`, `LapTime`, `CumulativeTime`    |
| `OvertakePerformed` | When a car passes another                    | `OvertakerId`, `DefenderId`, `Lap`             |
| `PitStopPerformed`  | When tire degradation triggers a pit stop    | `CarId`, `Lap`, `TimePenalty`                  |
| `IncidentOccurred`  | Random per-lap chance (difficulty-dependent) | `CarId`, `Description`, `TimePenalty`          |
| `CarRetired`        | When an incident causes a DNF                | `CarId`, `Description`                         |
| `WeatherChanged`    | Random mid-race toggle (sunny/rainy)         | `OldWeather`, `NewWeather`, `Lap`              |
| `RaceFinished`      | Once at end                                  | `Results[]` with positions and times           |

## Event ordering

Every simulation produces events in this order:

```
GridFormed → RaceStarted → [LapCompleted | PitStopPerformed | OvertakePerformed | IncidentOccurred | CarRetired | WeatherChanged]* → RaceFinished
```

## Generating entities

Generate individual domain objects without running a full simulation:

```csharp
var car     = ds.RaceCar(number: 1);    // single car with random name, pilot, spec
var grid    = ds.Grid(count: 12);        // starting grid of 12 cars
var circuit = ds.Circuit(CircuitDifficulty.Hard);
var conds   = ds.Conditions();           // random TimeOfDay + Weather
```

Use a locale to get localized pilot names and countries:

```csharp
var faker = new Faker("de");
var ds    = faker.ArcadeRacer();

var car     = ds.RaceCar(number: 1);    // pilot: "M. Schneider", etc.
var circuit = ds.Circuit();              // country: "Deutschland", etc.
```

## Simulation model

Car performance is driven by four stats (1-10): `Acceleration`, `TopSpeed`, `Handling`, `Endurance`.
Circuit layout (corners vs straights ratio) determines which stats matter most. Weather and
time-of-day modify stat effectiveness through a `DifficultyProfile`:

- **Rain** reduces top speed effectiveness, increases incident rate
- **Night** reduces handling effectiveness, increases incident rate
- **Hard circuits** have higher incident/weather change rates and slower base lap times
