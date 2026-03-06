// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Testing.Datasets.ArcadeRacer.Contracts;
using Testing.Datasets.ArcadeRacer.Model;

namespace Testing.Datasets.ArcadeRacer;


/// <summary>
/// Orchestrates a full arcade race simulation. Consumed as an <see cref="IAsyncEnumerable{T}"/>
/// that streams events one at a time with optional <see cref="Pacing"/> delay between each.
/// </summary>
public class ArcadeRaceSimulation : IAsyncEnumerable<IRaceEvent> {
    readonly ArcadeRaceOptions _options;
    readonly Faker             _faker;

    internal ArcadeRaceSimulation(ArcadeRaceOptions options, Faker? faker = null) {
        _options = options;
        _faker   = faker ?? new Faker();

        Pace = options.Pacing == TimeSpan.Zero
            ? static _ => Task.CompletedTask
            : ct => Task.WhenAny(Task.Delay(options.Pacing, ct));
    }

    Func<CancellationToken, Task> Pace  { get; init; }

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
}

/// <summary>Fluent builder for configuring and creating an <see cref="ArcadeRaceSimulation"/>.</summary>
public class ArcadeRaceBuilder {
    readonly Faker              _faker;
    readonly ArcadeRacerDataSet _ds;

    public ArcadeRaceBuilder() {
        _faker = new Faker();
        _ds    = _faker.ArcadeRacer();

        Options = new ArcadeRaceOptions {
            Cars       = _ds.Grid(_faker.Random.Int(8, 16)),
            Circuit    = _ds.Circuit(_faker.PickRandom<CircuitDifficulty>()),
            Conditions = _ds.Conditions(),
            TotalLaps  = _faker.Random.Int(5, 20),
            Pacing     = TimeSpan.Zero
        };
    }

    ArcadeRaceOptions Options { get; set; }

    /// <summary>Sets the number of cars on the starting grid.</summary>
    /// <param name="count">Number of cars to generate (default is 12).</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithCars(int count) {
        Options  = Options with { Cars = _ds.Grid(count) };
        return this;
    }

    /// <summary>Sets the total number of laps for the race.</summary>
    /// <param name="laps">Number of laps. Zero means unlimited.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithLaps(int laps) {
        Options = Options with { TotalLaps = laps };
        return this;
    }

    /// <summary>Sets the circuit difficulty tier.</summary>
    /// <param name="difficulty">Difficulty level for the generated circuit.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithDifficulty(CircuitDifficulty difficulty) {
        Options = Options with { Circuit = _ds.Circuit(difficulty) };
        return this;
    }

    /// <summary>Sets explicit weather and time-of-day conditions. Unset values are randomized at build time.</summary>
    /// <param name="timeOfDay">Time of day for the race.</param>
    /// <param name="weather">Weather condition for the race.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithConditions(TimeOfDay timeOfDay, Weather weather) {
        Options = Options with { Conditions = new RaceConditions(timeOfDay, weather) };
        return this;
    }

    /// <summary>Sets the delay between yielded events in streaming mode.</summary>
    /// <param name="pacing">Delay duration. <see cref="TimeSpan.Zero"/> means no delay.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithPacing(TimeSpan pacing) {
        Options = Options with { Pacing = pacing };
        return this;
    }

    /// <summary>Builds the <see cref="ArcadeRaceSimulation"/> with randomized defaults for any unset options.</summary>
    /// <returns>A configured simulation ready for streaming or resolution.</returns>
    public ArcadeRaceSimulation Create() {
        return new ArcadeRaceSimulation(Options, _faker);
    }
}

/// <summary>Static entry point for creating arcade race simulations.</summary>
public static class ArcadeRacer {
    /// <summary>Creates a new <see cref="ArcadeRaceBuilder"/> to configure and build a race simulation.</summary>
    /// <returns>A new builder instance.</returns>
    public static ArcadeRaceBuilder NewRace() => new();
}
