// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Tests.Datasets.ArcadeRacer;

[Category("ArcadeRacer")]
public class DifficultyProfileTests {
    [Test]
    public async ValueTask night_reduces_handling_effectiveness() {
        var day   = DifficultyProfile.For(CircuitDifficulty.Medium, new RaceConditions { TimeOfDay = TimeOfDay.Day, Weather = Weather.Sunny });
        var night = DifficultyProfile.For(CircuitDifficulty.Medium, new RaceConditions { TimeOfDay = TimeOfDay.Night, Weather = Weather.Sunny });

        await Assert.That(night.HandlingEffectiveness).IsLessThan(day.HandlingEffectiveness);
    }

    [Test]
    public async ValueTask rain_reduces_top_speed_effectiveness() {
        var sunny = DifficultyProfile.For(CircuitDifficulty.Medium, new RaceConditions { TimeOfDay = TimeOfDay.Day, Weather = Weather.Sunny });
        var rainy = DifficultyProfile.For(CircuitDifficulty.Medium, new RaceConditions { TimeOfDay = TimeOfDay.Day, Weather = Weather.Rainy });

        await Assert.That(rainy.TopSpeedEffectiveness).IsLessThan(sunny.TopSpeedEffectiveness);
    }

    [Test]
    public async ValueTask hard_difficulty_has_higher_incident_rate_than_easy() {
        var conditions = new RaceConditions { TimeOfDay = TimeOfDay.Day, Weather = Weather.Sunny };
        var easy       = DifficultyProfile.For(CircuitDifficulty.Easy, conditions);
        var hard       = DifficultyProfile.For(CircuitDifficulty.Hard, conditions);

        await Assert.That(hard.IncidentRate).IsGreaterThan(easy.IncidentRate);
    }
}
