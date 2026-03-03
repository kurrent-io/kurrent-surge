// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

/// <summary>Bogus <see cref="DataSet"/> that generates randomized cars, circuits, and race conditions.</summary>
public class ArcadeRacerDataSet(Faker? faker = null) : CustomDataSet<ArcadeRacerDataSet>(faker ?? new Faker()) {

    protected override void Initialize() { }

    #region Vocabulary

    static readonly string[] CarAdjectives = [
        "Thunder", "Shadow", "Blaze", "Storm", "Phantom",
        "Viper", "Turbo", "Apex", "Nitro", "Volt",
        "Fury", "Stealth", "Hyper", "Nova", "Titan"
    ];

    static readonly string[] CarNouns = [
        "Bolt", "Hawk", "Fury", "Fang", "Arrow",
        "Ghost", "Rocket", "Streak", "Strike", "Claw",
        "Flash", "Edge", "Blade", "Surge", "Drift"
    ];

    static readonly string[] CarSuffixes = [
        "MK-I", "MK-II", "MK-III", "MK-IV", "MK-V",
        "GT", "GTS", "RS", "Sport", "Pro", "Turbo"
    ];

    static readonly string[] CircuitPrefixes = [
        "Bay", "Grand", "Silver", "Iron", "Storm",
        "Eagle", "Royal", "Shadow", "Thunder", "Crystal",
        "Golden", "Red", "Black", "Emerald", "Azure"
    ];

    static readonly string[] CircuitSuffixes = [
        "shore", "vale", "park", "ring", "way",
        "wood", "heights", "point", "ridge", "crest",
        "gate", "field", "haven", "stone", "brook"
    ];

    internal static readonly string[] IncidentDescriptions = [
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

    #endregion

    #region Generators

    /// <summary>Generates a randomized <see cref="ArcadeRacer.RaceCar"/> with random name, pilot, and spec.</summary>
    /// <param name="number">Grid number assigned to the car.</param>
    /// <returns>A new <see cref="ArcadeRacer.RaceCar"/> with randomized attributes.</returns>
    public RaceCar RaceCar(int number = 1) {
        var adj    = Random.ArrayElement(CarAdjectives);
        var noun   = Random.ArrayElement(CarNouns);
        var suffix = Random.Bool() ? $" {Random.ArrayElement(CarSuffixes)}" : "";
        var first  = Faker.Name.FirstName();
        var last   = Faker.Name.LastName();

        return new RaceCar {
            Id     = Guid.NewGuid(),
            Name   = $"{adj} {noun}{suffix}",
            Number = number,
            Pilot  = $"{first[0]}. {last}",
            Spec = new RaceCarSpec(
                Acceleration: Random.Number(1, 10),
                TopSpeed:     Random.Number(1, 10),
                Handling:     Random.Number(1, 10),
                Endurance:    Random.Number(1, 10)
            )
        };
    }

    /// <summary>Generates a starting grid of randomized cars numbered 1 through <paramref name="count"/>.</summary>
    /// <param name="count">Number of cars on the grid.</param>
    /// <returns>A list of <see cref="ArcadeRacer.RaceCar"/> instances.</returns>
    public List<RaceCar> Grid(int count = 12) =>
        Enumerable.Range(1, count).Select(RaceCar).ToList();

    /// <summary>Generates a randomized <see cref="ArcadeRacer.Circuit"/> with the given difficulty.</summary>
    /// <param name="difficulty">Difficulty tier for the generated circuit.</param>
    /// <returns>A new <see cref="ArcadeRacer.Circuit"/> with randomized layout and name.</returns>
    public Circuit Circuit(CircuitDifficulty difficulty = CircuitDifficulty.Medium) {
        var prefix = Random.ArrayElement(CircuitPrefixes);
        var suffix = Random.ArrayElement(CircuitSuffixes);

        return new Circuit {
            Id         = Guid.NewGuid(),
            Name       = $"{prefix}{suffix}",
            Country    = Faker.Address.Country(),
            Corners    = Random.Number(4, 16),
            Straights  = Random.Number(3, 12),
            LengthKm   = Math.Round(Random.Double(3.0, 7.0), 3),
            Difficulty = difficulty
        };
    }

    /// <summary>Generates random <see cref="RaceConditions"/> with a random time of day and weather.</summary>
    /// <returns>A randomly selected combination of <see cref="TimeOfDay"/> and <see cref="Weather"/>.</returns>
    public RaceConditions Conditions() => new(
        TimeOfDay: Faker.PickRandom(TimeOfDay.Day, TimeOfDay.Night),
        Weather:   Faker.PickRandom(Weather.Sunny, Weather.Rainy)
    );

    #endregion
}
