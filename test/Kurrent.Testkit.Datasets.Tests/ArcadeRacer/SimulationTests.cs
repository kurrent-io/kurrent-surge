// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Testkit.Datasets.ArcadeRacer;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.Tests.ArcadeRacer;

[Category("ArcadeRacer")]
public class SimulationTests {
    readonly ArcadeRacerDataSet _ds = ArcadeRacerDataSet.Instance;

    static async Task<List<IRaceEvent>> CollectEvents(ArcadeRaceSimulation sim) {
        var events = new List<IRaceEvent>();

        await foreach (var e in sim)
            events.Add(e);

        return events;
    }

    #region Streaming mode

    [Test]
    public async Task streaming_produces_events() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        var events = await CollectEvents(sim);

        await Assert.That(events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task streaming_starts_with_grid_formed_then_race_started() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        var events = await CollectEvents(sim);

        await Assert.That(events[0]).IsTypeOf<GridFormed>();
        await Assert.That(events[1]).IsTypeOf<RaceStarted>();
    }

    [Test]
    public async Task streaming_ends_with_race_finished() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        var events = await CollectEvents(sim);

        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
    }

    [Test]
    public async Task streaming_race_finished_includes_all_cars() {
        var sim = _ds.NewRace()
            .WithCars(6)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        var events   = await CollectEvents(sim);
        var finished = events.OfType<RaceFinished>().Single();

        await Assert.That(finished.Results.Length).IsEqualTo(6);
    }

    [Test]
    public async Task streaming_contains_lap_completed_events() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithLaps(5)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        var events    = await CollectEvents(sim);
        var lapEvents = events.OfType<LapCompleted>().ToList();

        // At least 4 cars * 5 laps = 20 LapCompleted events (some may be missing due to DNF)
        await Assert.That(lapEvents.Count).IsGreaterThanOrEqualTo(10);
    }

    #endregion

    #region Endless mode

    [Test]
    public async Task endless_mode_runs_until_cancelled() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

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

    #region SimulateAsync

    [Test]
    public async Task simulate_async_returns_completed_race() {
        var race = await _ds.NewRace()
            .WithCars(6)
            .WithLaps(5)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .SimulateAsync();

        await Assert.That(race.Cars).Count().IsEqualTo(6);
        await Assert.That(race.TotalLaps).IsGreaterThanOrEqualTo(1);
        await Assert.That(race.Results).Count().IsEqualTo(6);
        await Assert.That(race.Events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task simulate_async_winner_is_first_position() {
        var race = await _ds.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .SimulateAsync();

        await Assert.That(race.Winner.Position).IsEqualTo(1);
        await Assert.That(race.Winner.Dnf).IsFalse();
    }

    [Test]
    public async Task simulate_async_logbook_contains_setup_and_finish() {
        var race = await _ds.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithDifficulty(CircuitDifficulty.Medium)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .SimulateAsync();

        await Assert.That(race.Circuit.Difficulty).IsEqualTo(CircuitDifficulty.Medium);
        await Assert.That(race.Conditions.TimeOfDay).IsEqualTo(TimeOfDay.Day);
        await Assert.That(race.Conditions.Weather).IsEqualTo(Weather.Sunny);
        await Assert.That(race.Events[0]).IsTypeOf<GridFormed>();
        await Assert.That(race.Events[^1]).IsTypeOf<RaceFinished>();
    }

    #endregion

    #region Result after streaming

    [Test]
    public async Task result_is_not_available_before_enumeration() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .Start();

        await Assert.That(sim.IsFinished).IsFalse();
        await Assert.That(() => sim.Result).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task result_is_available_after_enumeration() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        await foreach (var _ in sim) { }

        await Assert.That(sim.IsFinished).IsTrue();
        await Assert.That(sim.Result.Winner.Position).IsEqualTo(1);
        await Assert.That(sim.Result.Events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task result_is_available_after_endless_mode_cancellation() {
        var sim = _ds.NewRace()
            .WithCars(4)
            .WithEndlessLaps()
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Start();

        using var cts = new CancellationTokenSource();
        var maxLap = 0;

        await foreach (var e in sim.WithCancellation(cts.Token)) {
            if (e is LapCompleted lc)
                maxLap = Math.Max(maxLap, lc.Lap);

            if (maxLap >= 3)
                cts.Cancel();
        }

        await Assert.That(sim.IsFinished).IsTrue();
        await Assert.That(sim.Result.Cars.Length).IsEqualTo(4);
        await Assert.That(sim.Result.Results.Length).IsGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Conditions

    [Test]
    public async Task race_with_night_and_rain_has_incidents() {
        // Hard + night + rain gives ~21.6% incident rate per lap.
        // 50 laps makes zero incidents virtually impossible (p ≈ 0.0000005).
        var race = await _ds.NewRace()
            .WithCars(8)
            .WithLaps(50)
            .WithDifficulty(CircuitDifficulty.Hard)
            .WithConditions(TimeOfDay.Night, Weather.Rainy)
            .SimulateAsync();

        var incidents = race.Events.OfType<IncidentOccurred>().ToList();

        await Assert.That(incidents.Count).IsGreaterThan(0);
    }

    #endregion
}
