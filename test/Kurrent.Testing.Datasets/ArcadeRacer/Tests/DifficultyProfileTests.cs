// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Testing.Datasets.ArcadeRacer.Model;

namespace Testing.Datasets.ArcadeRacer.Tests;

[Category("ArcadeRacer")]
public class DifficultyProfileTests {
    [Test]
    public async Task night_reduces_handling_effectiveness() {
        var day   = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Day, Weather.Sunny));
        var night = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Night, Weather.Sunny));

        await Assert.That(night.HandlingEffectiveness).IsLessThan(day.HandlingEffectiveness);
    }

    [Test]
    public async Task rain_reduces_top_speed_effectiveness() {
        var sunny = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Day, Weather.Sunny));
        var rainy = DifficultyProfile.For(CircuitDifficulty.Medium, new(TimeOfDay.Day, Weather.Rainy));

        await Assert.That(rainy.TopSpeedEffectiveness).IsLessThan(sunny.TopSpeedEffectiveness);
    }

    [Test]
    public async Task hard_difficulty_has_higher_incident_rate_than_easy() {
        var conditions = new RaceConditions(TimeOfDay.Day, Weather.Sunny);
        var easy       = DifficultyProfile.For(CircuitDifficulty.Easy, conditions);
        var hard       = DifficultyProfile.For(CircuitDifficulty.Hard, conditions);

        await Assert.That(hard.IncidentRate).IsGreaterThan(easy.IncidentRate);
    }
}
