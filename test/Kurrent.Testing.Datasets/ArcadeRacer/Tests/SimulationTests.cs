// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Testing.Datasets.ArcadeRacer.Contracts;
using Testing.Datasets.ArcadeRacer.Model;

namespace Testing.Datasets.ArcadeRacer.Tests;

[Category("ArcadeRacer")]
public class SimulationTests {

    static async Task<List<IRaceEvent>> CollectEvents(ArcadeRaceSimulation sim) {
        var events = new List<IRaceEvent>();

        await foreach (var e in sim)
            events.Add(e);

        return events;
    }

    #region Streaming mode

    [Test]
    public async Task streaming_produces_events() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events = await CollectEvents(sim);

        await Assert.That(events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task streaming_starts_with_grid_formed_then_race_started() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events = await CollectEvents(sim);

        await Assert.That(events[0]).IsTypeOf<GridFormed>();
        await Assert.That(events[1]).IsTypeOf<RaceStarted>();
    }

    [Test]
    public async Task streaming_ends_with_race_finished() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events = await CollectEvents(sim);

        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
    }

    [Test]
    public async Task streaming_race_finished_includes_all_cars() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(6)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events   = await CollectEvents(sim);
        var finished = events.OfType<RaceFinished>().Single();

        await Assert.That(finished.Results.Length).IsEqualTo(6);
    }

    [Test]
    public async Task streaming_contains_lap_completed_events() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(4)
            .WithLaps(5)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events    = await CollectEvents(sim);
        var lapEvents = events.OfType<LapCompleted>().ToList();

        // At least 4 cars * 5 laps = 20 LapCompleted events (some may be missing due to DNF)
        await Assert.That(lapEvents.Count).IsGreaterThanOrEqualTo(10);
    }

    #endregion

    #region Endless mode

    [Test]
    public async Task endless_mode_runs_until_cancelled() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        using var cts    = new CancellationTokenSource();
        var events       = new List<IRaceEvent>();
        var maxLap       = 0;

        await foreach (var e in sim.WithCancellation(cts.Token)) {
            events.Add(e);

            if (e is LapCompleted lc)
                maxLap = Math.Max(maxLap, lc.Lap);

            if (maxLap >= 5)
                cts.Cancel();
        }

        await Assert.That(events[0]).IsTypeOf<GridFormed>();
        await Assert.That(events[1]).IsTypeOf<RaceStarted>();
        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
        await Assert.That(maxLap).IsGreaterThanOrEqualTo(5);
    }

    #endregion

    #region Conditions

    [Test]
    public async Task race_with_night_and_rain_has_incidents() {
        var sim = ArcadeRacer.NewRace()
            .WithCars(8)
            .WithLaps(20)
            .WithDifficulty(CircuitDifficulty.Hard)
            .WithConditions(TimeOfDay.Night, Weather.Rainy)
            .Create();

        var events    = await CollectEvents(sim);
        var incidents = events.OfType<IncidentOccurred>().ToList();

        await Assert.That(incidents.Count).IsGreaterThan(0);
    }

    #endregion
}
