// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;

public class RaceCarSpecFaker : Faker<RaceCarSpec> {
    public static readonly RaceCarSpecFaker Instance = new();

    public RaceCarSpecFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.Acceleration, f => f.Random.Int(1, 10));
        RuleFor(x => x.TopSpeed, f => f.Random.Int(1, 10));
        RuleFor(x => x.Handling, f => f.Random.Int(1, 10));
        RuleFor(x => x.Endurance, f => f.Random.Int(1, 10));
    }
}

public class RaceCarFaker : Faker<RaceCar> {
    public static readonly RaceCarFaker Instance = new();

    internal static readonly string[] Adjectives = [
        "Thunder", "Shadow", "Blaze", "Storm", "Phantom",
        "Viper", "Turbo", "Apex", "Nitro", "Volt",
        "Fury", "Stealth", "Hyper", "Nova", "Titan"
    ];

    internal static readonly string[] Nouns = [
        "Bolt", "Hawk", "Fury", "Fang", "Arrow",
        "Ghost", "Rocket", "Streak", "Strike", "Claw",
        "Flash", "Edge", "Blade", "Surge", "Drift"
    ];

    internal static readonly string[] Suffixes = [
        "MK-I", "MK-II", "MK-III", "MK-IV", "MK-V",
        "GT", "GTS", "RS", "Sport", "Pro", "Turbo"
    ];

    public RaceCarFaker(Faker? faker = null) {
        faker ??= new Faker();
        var spec = new RaceCarSpecFaker(faker);

        RuleFor(x => x.Id, f => PunyId.New());
        RuleFor(x => x.Name, f => {
            var adj  = f.PickRandom(Adjectives);
            var noun = f.PickRandom(Nouns);
            var sfx  = f.Random.Bool() ? $" {f.PickRandom(Suffixes)}" : "";
            return $"{adj} {noun}{sfx}";
        });
        RuleFor(x => x.Number, f => f.IndexFaker + 1);
        RuleFor(x => x.Pilot, f => $"{f.Name.FirstName()[0]}. {f.Name.LastName()}");
        RuleFor(x => x.Spec, f => spec.Generate());
    }
}

public class CircuitFaker : Faker<Circuit> {
    public static readonly CircuitFaker Instance = new();

    internal static readonly string[] Prefixes = [
        "Bay", "Grand", "Silver", "Iron", "Storm",
        "Eagle", "Royal", "Shadow", "Thunder", "Crystal",
        "Golden", "Red", "Black", "Emerald", "Azure"
    ];

    internal static readonly string[] Suffixes = [
        "shore", "vale", "park", "ring", "way",
        "wood", "heights", "point", "ridge", "crest",
        "gate", "field", "haven", "stone", "brook"
    ];

    public CircuitFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.Id, f => PunyId.New());
        RuleFor(x => x.Name, f => $"{f.PickRandom(Prefixes)}{f.PickRandom(Suffixes)}");
        RuleFor(x => x.Country, f => f.Address.Country());
        RuleFor(x => x.Corners, f => f.Random.Int(4, 16));
        RuleFor(x => x.Straights, f => f.Random.Int(3, 12));
        RuleFor(x => x.LengthKm, f => Math.Round(f.Random.Double(3.0, 7.0), 3));
        RuleFor(x => x.Difficulty, f => f.PickRandom<CircuitDifficulty>());
    }
}

public class RaceConditionsFaker : Faker<RaceConditions> {
    public static readonly RaceConditionsFaker Instance = new();

    public RaceConditionsFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.TimeOfDay, f => f.PickRandom(TimeOfDay.Day, TimeOfDay.Night));
        RuleFor(x => x.Weather, f => f.PickRandom(Weather.Sunny, Weather.Rainy));
    }
}

public class RaceResultFaker : Faker<RaceResult> {
    public static readonly RaceResultFaker Instance = new();

    public RaceResultFaker(Faker? faker = null) {
        faker ??= new Faker();
        RuleFor(x => x.CarId, f => PunyId.New());
        RuleFor(x => x.CarName, f => $"{f.PickRandom(RaceCarFaker.Adjectives)} {f.PickRandom(RaceCarFaker.Nouns)}");
        RuleFor(x => x.Pilot, f => $"{f.Name.FirstName()[0]}. {f.Name.LastName()}");
        RuleFor(x => x.Position, f => f.IndexFaker + 1);
        RuleFor(x => x.Dnf, f => false);
        RuleFor(x => x.CumulativeTime, f => Math.Round(f.Random.Double(1200, 6000), 3));
        RuleFor(x => x.GapToLeader, f => Math.Round(f.Random.Double(0, 60), 3));
        RuleFor(x => x.LapsCompleted, f => f.Random.Int(10, 50));
    }
}
