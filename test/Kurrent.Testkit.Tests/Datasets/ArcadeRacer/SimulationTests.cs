// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Testkit.Datasets.ArcadeRacer;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Tests.Datasets.ArcadeRacer;

[Category("ArcadeRacer")]
public class SimulationTests {

    #region ->> Pre-computed (Simulate) <<-

    [Test]
    public async ValueTask simulate_returns_completed_race() {
        // Arrange & Act
        var race = ArcadeRaceSimulator.NewRace()
            .WithCars(6)
            .WithLaps(5)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Simulate();

        // Assert
        await Assert.That(race.Cars).Count().IsEqualTo(6);
        await Assert.That(race.TotalLaps).IsGreaterThanOrEqualTo(1);
        await Assert.That(race.Results).Count().IsEqualTo(6);
        await Assert.That(race.Events.Count).IsGreaterThan(0);
    }

    [Test]
    public async ValueTask simulate_winner_is_first_position() {
        // Arrange & Act
        var race = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Simulate();

        // Assert
        await Assert.That(race.Winner.Position).IsEqualTo(1);
        await Assert.That(race.Winner.Dnf).IsFalse();
    }

    [Test]
    public async ValueTask simulate_logbook_contains_setup_and_finish() {
        // Arrange & Act
        var race = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithDifficulty(CircuitDifficulty.Medium)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Simulate();

        // Assert
        await Assert.That(race.Circuit.Difficulty).IsEqualTo(CircuitDifficulty.Medium);
        await Assert.That(race.Conditions.TimeOfDay).IsEqualTo(TimeOfDay.Day);
        await Assert.That(race.Conditions.Weather).IsEqualTo(Weather.Sunny);
        await Assert.That(race.Events[0]).IsTypeOf<GridFormed>();
        await Assert.That(race.Events[^1]).IsTypeOf<RaceFinished>();
    }

    [Test]
    public async ValueTask simulate_rejects_endless_laps() {
        // Arrange
        var builder = ArcadeRaceSimulator.NewRace().WithLaps(0);

        // Act & Assert
        await Assert.That(() => builder.Simulate()).Throws<InvalidOperationException>();
    }

    #endregion

    #region ->> Streaming <<-

    [Test]
    public async ValueTask streaming_produces_events() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using var sim = ArcadeRaceSimulator.NewStreamingRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        // Act
        var events = await sim.RaceEvents(ct: cts.Token).Take(10).ToListAsync();

        // Assert
        await Assert.That(events.Count).IsEqualTo(10);
    }

    [Test]
    public async ValueTask streaming_starts_with_grid_formed_then_race_started() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using var sim = ArcadeRaceSimulator.NewStreamingRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        // Act
        var events = await sim.RaceEvents(ct: cts.Token).Take(2).ToListAsync();

        // Assert
        await Assert.That(events[0]).IsTypeOf<GridFormed>();
        await Assert.That(events[1]).IsTypeOf<RaceStarted>();
    }

    [Test]
    public async ValueTask streaming_contains_lap_completed_events() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using var sim = ArcadeRaceSimulator.NewStreamingRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        // Act
        var events    = await sim.RaceEvents(ct: cts.Token).Take(100).ToListAsync();
        var lapEvents = events.OfType<LapCompleted>().ToList();

        // Assert
        await Assert.That(lapEvents.Count).IsGreaterThanOrEqualTo(10);
    }

    [Test]
    public async ValueTask streaming_with_race_finished_on_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using var sim = ArcadeRaceSimulator.NewStreamingRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        // Act — read events, cancel after 5 laps, include RaceFinished
        var events = new List<IRaceEvent>();
        var maxLap = 0;

        await foreach (var e in sim.RaceEvents(includeRaceFinished: true, ct: cts.Token)) {
            events.Add(e);

            if (e is LapCompleted lc)
                maxLap = Math.Max(maxLap, lc.Lap);

            if (maxLap >= 5)
                await cts.CancelAsync();
        }

        // Assert
        await Assert.That(events[0]).IsTypeOf<GridFormed>();
        await Assert.That(events[1]).IsTypeOf<RaceStarted>();
        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
        await Assert.That(maxLap).IsGreaterThanOrEqualTo(5);
    }

    [Test]
    public async ValueTask streaming_without_race_finished_on_cancellation() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using var sim = ArcadeRaceSimulator.NewStreamingRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        // Act — read 10 events without RaceFinished
        var events = await sim.RaceEvents(includeRaceFinished: false, ct: cts.Token).Take(10).ToListAsync();

        // Assert — no RaceFinished
        await Assert.That(events.OfType<RaceFinished>().Count()).IsEqualTo(0);
        await Assert.That(events.Count).IsEqualTo(10);
    }

    #endregion

    #region ->> Conditions <<-

    [Test]
    public async ValueTask race_with_night_and_rain_has_incidents() {
        // Arrange & Act
        var race = ArcadeRaceSimulator.NewRace()
            .WithCars(8)
            .WithLaps(50)
            .WithDifficulty(CircuitDifficulty.Hard)
            .WithConditions(TimeOfDay.Night, Weather.Rainy)
            .Simulate();

        var incidents = race.Events.OfType<IncidentOccurred>().ToList();

        // Assert
        await Assert.That(incidents.Count).IsGreaterThan(0);
    }

    #endregion
}
