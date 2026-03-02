// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer.Tests;

[Category("ArcadeRacer")]
public class SimulationTests {

    #region Resolve mode

    [Test]
    public async Task resolve_produces_events() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(5)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var (_, events) = sim.Resolve();

        await Assert.That(events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task resolve_starts_with_race_started() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var (_, events) = sim.Resolve();

        await Assert.That(events[0]).IsTypeOf<RaceStarted>();
    }

    [Test]
    public async Task resolve_ends_with_race_finished() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var (_, events) = sim.Resolve();

        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
    }

    [Test]
    public async Task resolve_race_finished_includes_all_cars() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(6)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var (_, events)  = sim.Resolve();
        var finished     = events.OfType<RaceFinished>().Single();

        await Assert.That(finished.Results.Length).IsEqualTo(6);
    }

    [Test]
    public async Task resolve_throws_for_endless_mode() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .Create();

        var act = () => sim.Resolve();

        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task resolve_contains_lap_completed_events() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(5)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var (_, events) = sim.Resolve();
        var lapEvents   = events.OfType<LapCompleted>().ToList();

        // At least 4 cars * 5 laps = 20 LapCompleted events (some may be missing due to DNF)
        await Assert.That(lapEvents.Count).IsGreaterThanOrEqualTo(10);
    }

    #endregion

    #region Streaming mode

    [Test]
    public async Task streaming_produces_events() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events = new List<IRaceEvent>();

        await foreach (var e in sim)
            events.Add(e);

        await Assert.That(events.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task streaming_starts_with_race_started() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events = new List<IRaceEvent>();

        await foreach (var e in sim)
            events.Add(e);

        await Assert.That(events[0]).IsTypeOf<RaceStarted>();
    }

    [Test]
    public async Task streaming_ends_with_race_finished() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(2)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var events = new List<IRaceEvent>();

        await foreach (var e in sim)
            events.Add(e);

        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
    }

    #endregion

    #region Endless mode

    [Test]
    public async Task endless_mode_runs_until_cancelled() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        using var cts    = new CancellationTokenSource();
        var events       = new List<IRaceEvent>();
        var lapCount     = 0;

        await foreach (var e in sim.WithCancellation(cts.Token)) {
            events.Add(e);

            if (e is LapCompleted { Position: 1 })
                lapCount++;

            if (lapCount >= 5)
                cts.Cancel();
        }

        // Should have RaceStarted + lap events + RaceFinished
        await Assert.That(events[0]).IsTypeOf<RaceStarted>();
        await Assert.That(events[^1]).IsTypeOf<RaceFinished>();
        await Assert.That(lapCount).IsGreaterThanOrEqualTo(5);
    }

    #endregion

    #region Conditions

    [Test]
    public async Task race_with_night_and_rain_has_incidents() {
        var sim = ArcadeRaceSimulator.NewRace()
            .WithCars(8)
            .WithLaps(20)
            .WithDifficulty(CircuitDifficulty.Hard)
            .WithConditions(TimeOfDay.Night, Weather.Rainy)
            .Create();

        var (_, events) = sim.Resolve();
        var incidents   = events.OfType<IncidentOccurred>().ToList();

        await Assert.That(incidents.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task resolve_and_streaming_produce_same_event_types() {
        var create = () => ArcadeRaceSimulator.NewRace()
            .WithCars(4)
            .WithLaps(3)
            .WithConditions(TimeOfDay.Day, Weather.Sunny)
            .Create();

        var (_, resolveEvents) = create().Resolve();

        var streamEvents = new List<IRaceEvent>();

        await foreach (var e in create())
            streamEvents.Add(e);

        // Both should start with RaceStarted and end with RaceFinished
        await Assert.That(resolveEvents[0]).IsTypeOf<RaceStarted>();
        await Assert.That(streamEvents[0]).IsTypeOf<RaceStarted>();
        await Assert.That(resolveEvents[^1]).IsTypeOf<RaceFinished>();
        await Assert.That(streamEvents[^1]).IsTypeOf<RaceFinished>();
    }

    #endregion
}
