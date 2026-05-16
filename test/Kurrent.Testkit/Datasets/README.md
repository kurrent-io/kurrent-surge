# Kurrent.Testing.Datasets

Bogus-based data generation library for integration and unit tests. Each dataset provides domain models, typed
events, Faker-driven generators, and optional simulation engines that produce realistic `IAsyncEnumerable` event
streams.

## Structure

Each dataset follows a consistent layout:

```
{DatasetName}/
  Model/          Domain types, value objects, enums
  Contracts/      Event interfaces and records
  Fakers/         Individual Bogus Faker<T> classes (optional)
  Tests/          TUnit tests for the dataset
  {Name}DataSet.cs        Bogus DataSet entry point
  FakerExtensions.cs      C# 14 extension on Faker
  {Name}Simulation.cs     IAsyncEnumerable simulation engine (optional)
```

Root namespace: `Testing.Datasets`. Each dataset extends it: `Testing.Datasets.{DatasetName}`.
Model types live under `Testing.Datasets.{DatasetName}.Model`.

## Datasets

| Dataset        | Description                                               | Simulation |
|----------------|-----------------------------------------------------------|------------|
| ArcadeRacer    | Arcade racing with circuits, cars, laps, and weather      | Yes        |
| HomeAutomation | Smart home devices, rooms, and sensor events              | Yes        |

## Usage

```csharp
// Via DataSet directly
var home = HomeAutomationDataSet.Default.SmartHome();

// Via Faker extension
var faker = new Faker();
var race = faker.ArcadeRacer().Race();

// Via simulation stream (ArcadeRacer)
await foreach (var evt in ArcadeRaceSimulation.Create().MaxEvents(100).Build())
    Console.WriteLine(evt);

// Via simulation stream (HomeAutomation)
await foreach (var evt in HomeSimulator.NewSimulation().MaxEvents(100).Create())
    Console.WriteLine(evt);
```
