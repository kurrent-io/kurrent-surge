// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;

public class GridFormedFaker : Faker<GridFormed> {
    public static readonly GridFormedFaker Instance = new();

    public GridFormedFaker(Faker? faker = null) {
        faker ??= new Faker();
        var carFaker  = RaceCar.New(faker);
        var circFaker = Circuit.New(faker);
        var condFaker = new RaceConditionsFaker(faker);

        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.Cars, f => carFaker.Generate(f.Random.Int(4, 12)).ToArray());
        RuleFor(x => x.Circuit, f => circFaker.Generate());
        RuleFor(x => x.Conditions, f => condFaker.Generate());
        RuleFor(x => x.TotalLaps, f => f.Random.Int(5, 20));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class RaceStartedFaker : Faker<RaceStarted> {
    public static readonly RaceStartedFaker Instance = new();

    public RaceStartedFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class LapCompletedFaker : Faker<LapCompleted> {
    public static readonly LapCompletedFaker Instance = new();

    public LapCompletedFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.CarId, f => TestId.New());
        RuleFor(x => x.Lap, f => f.Random.Int(1, 50));
        RuleFor(x => x.LapTime, f => Math.Round(f.Random.Double(60, 120), 3));
        RuleFor(x => x.CumulativeTime, f => Math.Round(f.Random.Double(120, 6000), 3));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class OvertakePerformedFaker : Faker<OvertakePerformed> {
    public static readonly OvertakePerformedFaker Instance = new();

    public OvertakePerformedFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.OvertakerId, f => TestId.New());
        RuleFor(x => x.DefenderId, f => TestId.New());
        RuleFor(x => x.Lap, f => f.Random.Int(1, 50));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class PitStopPerformedFaker : Faker<PitStopPerformed> {
    public static readonly PitStopPerformedFaker Instance = new();

    public PitStopPerformedFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.CarId, f => TestId.New());
        RuleFor(x => x.Lap, f => f.Random.Int(1, 50));
        RuleFor(x => x.TimePenalty, f => Math.Round(f.Random.Double(15, 30), 3));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class IncidentOccurredFaker : Faker<IncidentOccurred> {
    public static readonly IncidentOccurredFaker Instance = new();

    internal static readonly string[] Descriptions = [
        "spun off at corner exit",
        "mechanical failure",
        "collision with barrier",
        "engine stall on straight",
        "brake failure into corner",
        "suspension damage from curb",
        "gearbox malfunction",
        "hydraulic leak",
        "electrical failure",
        "tire puncture",
        "collision with debris",
        "overheated engine"
    ];

    public IncidentOccurredFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.CarId, f => TestId.New());
        RuleFor(x => x.Lap, f => f.Random.Int(1, 50));
        RuleFor(x => x.Description, f => f.PickRandom(Descriptions));
        RuleFor(x => x.TimePenalty, f => Math.Round(f.Random.Double(2, 15), 3));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class CarRetiredFaker : Faker<CarRetired> {
    public static readonly CarRetiredFaker Instance = new();

    public CarRetiredFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.CarId, f => TestId.New());
        RuleFor(x => x.Lap, f => f.Random.Int(1, 50));
        RuleFor(x => x.Description, f => f.PickRandom(IncidentOccurredFaker.Descriptions));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class WeatherChangedFaker : Faker<WeatherChanged> {
    public static readonly WeatherChangedFaker Instance = new();

    public WeatherChangedFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.OldWeather, f => f.PickRandom(Weather.Sunny, Weather.Rainy));
        RuleFor(x => x.NewWeather, f => f.PickRandom(Weather.Sunny, Weather.Rainy));
        RuleFor(x => x.Lap, f => f.Random.Int(1, 50));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}

public class RaceFinishedFaker : Faker<RaceFinished> {
    public static readonly RaceFinishedFaker Instance = new();

    public RaceFinishedFaker(Faker? faker = null) {
        faker ??= new Faker();
        var resultFaker = new RaceResultFaker(faker);

        RuleFor(x => x.EventId, f => Guid.CreateVersion7());
        RuleFor(x => x.Results, f => resultFaker.Generate(f.Random.Int(4, 12)).ToArray());
        RuleFor(x => x.TotalLaps, f => f.Random.Int(10, 50));
        RuleFor(x => x.Timestamp, f => f.Date.RecentOffset().ToUnixTimeMilliseconds());
    }
}
