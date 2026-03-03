// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;

namespace Kurrent.Testing.Bogus.Datasets.ArcadeRacer;


/// <summary>
/// Orchestrates a full arcade race simulation. Consumed as an <see cref="IAsyncEnumerable{T}"/>
/// that streams events one at a time with optional <see cref="Pacing"/> delay between each.
/// </summary>
public class ArcadeRaceSimulation : IAsyncEnumerable<IRaceEvent> {
    readonly ArcadeRaceOptions _options;
    readonly Faker             _faker;

    internal ArcadeRaceSimulation(ArcadeRaceOptions options, Faker faker) {
        _options = options;
        _faker   = faker;
    }

    /// <summary>Delay between yielded events in streaming mode. Zero means no delay.</summary>
    public TimeSpan Pacing { get; init; }

    /// <summary>Streams race events one at a time with optional <see cref="Pacing"/> delay between each.</summary>
    /// <param name="ct">Cancellation token checked between laps.</param>
    /// <returns>An async enumerator yielding <see cref="IRaceEvent"/> instances as the race progresses.</returns>
    public async IAsyncEnumerator<IRaceEvent> GetAsyncEnumerator(CancellationToken ct = default) {
        var race = new ArcadeRace(_faker);

        foreach (var e in race.FormGrid(_options)) {
            yield return e;
            await Pace(ct);
        }

        foreach (var e in race.Start()) {
            yield return e;
            await Pace(ct);
        }

        while (race.State.IsActive && (race.State.TotalLaps == 0 || race.State.Lap < race.State.TotalLaps)) {
            foreach (var e in race.SimulateLap()) {
                yield return e;
                await Pace(ct);
            }

            foreach (var e in race.CheckWeather()) {
                yield return e;
                await Pace(ct);
            }

            foreach (var e in race.CheckIncident()) {
                yield return e;
                await Pace(ct);
            }

            if (ct.IsCancellationRequested) break;
        }

        foreach (var e in race.Finish())
            yield return e;
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

/// <summary>Fluent builder for configuring and creating an <see cref="ArcadeRaceSimulation"/>.</summary>
public class ArcadeRaceBuilder {
    ArcadeRaceOptions _options    = new();
    RaceConditions    _conditions = RaceConditions.Unspecified;
    CircuitDifficulty _difficulty = CircuitDifficulty.Medium;
    TimeSpan          _pacing     = TimeSpan.Zero;
    int               _carCount   = 12;

    /// <summary>Sets the number of cars on the starting grid.</summary>
    /// <param name="count">Number of cars to generate (default is 12).</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithCars(int count) {
        _carCount = count;
        return this;
    }

    /// <summary>Sets the total number of laps for the race.</summary>
    /// <param name="laps">Number of laps. Zero means unlimited.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithLaps(int laps) {
        _options = _options with { TotalLaps = laps };
        return this;
    }

    /// <summary>Sets the circuit difficulty tier.</summary>
    /// <param name="difficulty">Difficulty level for the generated circuit.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithDifficulty(CircuitDifficulty difficulty) {
        _difficulty = difficulty;
        return this;
    }

    /// <summary>Sets explicit weather and time-of-day conditions. Unset values are randomized at build time.</summary>
    /// <param name="timeOfDay">Time of day for the race.</param>
    /// <param name="weather">Weather condition for the race.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithConditions(TimeOfDay timeOfDay, Weather weather) {
        _conditions = new RaceConditions(timeOfDay, weather);
        return this;
    }

    /// <summary>Sets the delay between yielded events in streaming mode.</summary>
    /// <param name="pacing">Delay duration. <see cref="TimeSpan.Zero"/> means no delay.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithPacing(TimeSpan pacing) {
        _pacing = pacing;
        return this;
    }

    /// <summary>Builds the <see cref="ArcadeRaceSimulation"/> with randomized defaults for any unset options.</summary>
    /// <returns>A configured simulation ready for streaming or resolution.</returns>
    public ArcadeRaceSimulation Create() {
        var faker = BogusFaker.Shared;
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

/// <summary>Static entry point for creating arcade race simulations.</summary>
public static class ArcadeRacer {
    /// <summary>Creates a new <see cref="ArcadeRaceBuilder"/> to configure and build a race simulation.</summary>
    /// <returns>A new builder instance.</returns>
    public static ArcadeRaceBuilder NewRace() => new();
}
