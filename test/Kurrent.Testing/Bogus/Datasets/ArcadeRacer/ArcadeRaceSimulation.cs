// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;

public static class ArcadeRaceSimulator {
    public static ArcadeRaceBuilder NewRace() => new();
}

public class ArcadeRaceSimulation : IAsyncEnumerable<IRaceEvent> {
    readonly ArcadeRaceOptions _options;
    readonly Faker             _faker;

    internal ArcadeRaceSimulation(ArcadeRaceOptions options, Faker faker) {
        _options = options;
        _faker   = faker;
    }

    public TimeSpan Pacing { get; init; }

    public async IAsyncEnumerator<IRaceEvent> GetAsyncEnumerator(CancellationToken ct = default) {
        var race = ArcadeRace.Initialize(_options, _faker);
        ArcadeRaceState state;
        IRaceEvent[] events;

        (state, events) = race.Start();
        foreach (var e in events) { yield return e; await Pace(ct); }

        while (state.IsActive && (state.TotalLaps == 0 || state.Lap <= state.TotalLaps)) {
            race.AdvanceTimestamp();

            (state, events) = race.CheckWeather();
            foreach (var e in events) { yield return e; await Pace(ct); }

            (state, events) = race.SimulateLap();
            foreach (var e in events) { yield return e; await Pace(ct); }

            (state, events) = race.CheckIncident();
            foreach (var e in events) { yield return e; await Pace(ct); }

            if (ct.IsCancellationRequested) break;

            race.IncrementLap();
        }

        (state, events) = race.Finish();
        foreach (var e in events) yield return e;
    }

    public (ArcadeRaceState, List<IRaceEvent>) Resolve() {
        if (_options.TotalLaps == 0)
            throw new InvalidOperationException("Resolve() requires a finite lap count. Use WithLaps() or stream mode for endless races.");

        var race      = ArcadeRace.Initialize(_options, _faker);
        var allEvents = new List<IRaceEvent>();
        ArcadeRaceState state;
        IRaceEvent[] events;

        (state, events) = race.Start();
        allEvents.AddRange(events);

        while (state.IsActive && state.Lap <= state.TotalLaps) {
            race.AdvanceTimestamp();

            (state, events) = race.CheckWeather();
            allEvents.AddRange(events);

            (state, events) = race.SimulateLap();
            allEvents.AddRange(events);

            (state, events) = race.CheckIncident();
            allEvents.AddRange(events);

            race.IncrementLap();
        }

        (state, events) = race.Finish();
        allEvents.AddRange(events);

        return (state, allEvents);
    }

    #region Pacing

    async ValueTask Pace(CancellationToken ct) {
        if (Pacing == TimeSpan.Zero) return;

        try {
            await Task.Delay(Pacing, ct);
        }
        catch (OperationCanceledException) {
            // Cancellation is checked between laps, not here
        }
    }

    #endregion
}

public class ArcadeRaceBuilder {
    ArcadeRaceOptions _options    = new();
    RaceConditions    _conditions = RaceConditions.Unspecified;
    CircuitDifficulty _difficulty = CircuitDifficulty.Medium;
    TimeSpan          _pacing     = TimeSpan.Zero;
    int               _carCount  = 12;

    public ArcadeRaceBuilder WithCars(int count) {
        _carCount = count;
        return this;
    }

    public ArcadeRaceBuilder WithLaps(int laps) {
        _options = _options with { TotalLaps = laps };
        return this;
    }

    public ArcadeRaceBuilder WithDifficulty(CircuitDifficulty difficulty) {
        _difficulty = difficulty;
        return this;
    }

    public ArcadeRaceBuilder WithConditions(TimeOfDay timeOfDay, Weather weather) {
        _conditions = new RaceConditions(timeOfDay, weather);
        return this;
    }

    public ArcadeRaceBuilder WithPacing(TimeSpan pacing) {
        _pacing = pacing;
        return this;
    }

    public ArcadeRaceSimulation Create() {
        var faker = new Faker();
        var ds    = new ArcadeRacerDataSet(faker);

        var conditions = _conditions;

        if (conditions.TimeOfDay == TimeOfDay.Unspecified)
            conditions = conditions with { TimeOfDay = faker.PickRandom(TimeOfDay.Day, TimeOfDay.Night) };

        if (conditions.Weather == Weather.Unspecified)
            conditions = conditions with { Weather = faker.PickRandom(Weather.Sunny, Weather.Rainy) };

        var resolved = _options with {
            Cars       = ds.Grid(_carCount),
            Circuit    = ds.Circuit(_difficulty),
            Conditions = conditions
        };

        return new ArcadeRaceSimulation(resolved, faker) { Pacing = _pacing };
    }
}
