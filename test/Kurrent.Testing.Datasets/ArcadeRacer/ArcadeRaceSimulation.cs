// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer;


/// <summary>
/// Orchestrates a full arcade race simulation. Consumed as an <see cref="IAsyncEnumerable{T}"/>
/// that streams events one at a time with optional pacing delay between each.
/// After enumeration completes, the <see cref="Result"/> property contains the full race logbook.
/// </summary>
public class ArcadeRaceSimulation : IAsyncEnumerable<IRaceEvent> {
    readonly ArcadeRaceOptions  _options;
    readonly Faker              _faker;
    readonly List<IRaceEvent>   _events = [];
    CompletedRace?              _result;

    internal ArcadeRaceSimulation(ArcadeRaceOptions options, Faker? faker = null) {
        _options = options;
        _faker   = faker ?? new Faker();

        Pace = options.Pacing == TimeSpan.Zero
            ? static _ => Task.CompletedTask
            : ct => Task.WhenAny(Task.Delay(options.Pacing, ct));
    }

    Func<CancellationToken, Task> Pace { get; init; }

    /// <summary>True once the race has finished and <see cref="Result"/> is available.</summary>
    public bool IsFinished => _result is not null;

    /// <summary>
    /// The complete race logbook. Available after enumeration completes.
    /// Throws <see cref="InvalidOperationException"/> if the race has not finished yet.
    /// </summary>
    public CompletedRace Result => _result
        ?? throw new InvalidOperationException("The race has not finished yet. Enumerate the simulation first.");

    /// <summary>Streams race events one at a time with optional pacing delay between each.</summary>
    /// <param name="ct">Cancellation token checked between laps.</param>
    /// <returns>An async enumerator yielding <see cref="IRaceEvent"/> instances as the race progresses.</returns>
    public async IAsyncEnumerator<IRaceEvent> GetAsyncEnumerator(CancellationToken ct = default) {
        var race = new ArcadeRace(_faker);

        foreach (var e in race.FormGrid(_options)) {
            _events.Add(e);
            yield return e;
            await Pace(ct);
        }

        foreach (var e in race.Start()) {
            _events.Add(e);
            yield return e;
            await Pace(ct);
        }

        while (race.State.IsActive && (race.State.TotalLaps == 0 || race.State.Lap < race.State.TotalLaps)) {
            foreach (var e in race.SimulateLap()) {
                _events.Add(e);
                yield return e;
                await Pace(ct);
            }

            foreach (var e in race.CheckWeather()) {
                _events.Add(e);
                yield return e;
                await Pace(ct);
            }

            foreach (var e in race.CheckIncident()) {
                _events.Add(e);
                yield return e;
                await Pace(ct);
            }

            if (ct.IsCancellationRequested) break;
        }

        foreach (var e in race.Finish()) {
            _events.Add(e);
            yield return e;
        }

        var gridFormed = _events.OfType<GridFormed>().Single();
        var finished   = _events.OfType<RaceFinished>().Single();

        _result = new CompletedRace {
            Cars       = gridFormed.Cars,
            Circuit    = gridFormed.Circuit,
            Conditions = gridFormed.Conditions,
            Results    = finished.Results,
            TotalLaps  = finished.TotalLaps,
            Events     = _events.AsReadOnly()
        };
    }
}

/// <summary>Fluent builder for configuring and creating an <see cref="ArcadeRaceSimulation"/>.</summary>
public class ArcadeRaceBuilder {
    readonly Faker              _faker;
    readonly ArcadeRacerDataSet _ds;

    public ArcadeRaceBuilder() : this(new Faker()) { }

    internal ArcadeRaceBuilder(Faker faker) {
        _faker = faker;
        _ds    = new ArcadeRacerDataSet(faker);

        Options = new ArcadeRaceOptions {
            Cars       = _ds.RaceCar.Generate(_faker.Random.Int(8, 16)),
            Circuit    = _ds.Circuit.Generate(),
            Conditions = new RaceConditionsFaker(_faker).Generate(),
            TotalLaps  = _faker.Random.Int(5, 20),
            Pacing     = TimeSpan.Zero
        };
    }

    ArcadeRaceOptions Options { get; set; }

    /// <summary>Sets the number of cars on the starting grid.</summary>
    /// <param name="count">Number of cars to generate (default is 12).</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithCars(int count) {
        Options = Options with { Cars = _ds.RaceCar.Generate(count) };
        return this;
    }

    /// <summary>Sets the total number of laps for the race.</summary>
    /// <param name="laps">Number of laps. Zero means unlimited.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithLaps(int laps) {
        Options = Options with { TotalLaps = laps };
        return this;
    }

    /// <summary>Configures the race to run indefinitely until cancelled.</summary>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithEndlessLaps() {
        Options = Options with { TotalLaps = 0 };
        return this;
    }

    /// <summary>Sets the circuit difficulty tier.</summary>
    /// <param name="difficulty">Difficulty level for the generated circuit.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithDifficulty(CircuitDifficulty difficulty) {
        Options = Options with {
            Circuit = Circuit.New(_faker).RuleFor(x => x.Difficulty, difficulty).Generate()
        };
        return this;
    }

    /// <summary>Sets explicit weather and time-of-day conditions. Unset values are randomized at build time.</summary>
    /// <param name="timeOfDay">Time of day for the race.</param>
    /// <param name="weather">Weather condition for the race.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithConditions(TimeOfDay timeOfDay, Weather weather) {
        Options = Options with { Conditions = new RaceConditions { TimeOfDay = timeOfDay, Weather = weather } };
        return this;
    }

    /// <summary>Sets the delay between yielded events in streaming mode.</summary>
    /// <param name="pacing">Delay duration. <see cref="TimeSpan.Zero"/> means no delay.</param>
    /// <returns>This builder for chaining.</returns>
    public ArcadeRaceBuilder WithPacing(TimeSpan pacing) {
        Options = Options with { Pacing = pacing };
        return this;
    }

    /// <summary>Starts the simulation for streaming events one at a time via <c>await foreach</c>.</summary>
    /// <returns>A simulation whose <see cref="ArcadeRaceSimulation.Result"/> is available after enumeration.</returns>
    public ArcadeRaceSimulation Start() => new(Options, _faker);

    /// <summary>Runs the full simulation to completion and returns the race logbook.</summary>
    /// <returns>A <see cref="CompletedRace"/> containing setup, standings, and all events.</returns>
    public async Task<CompletedRace> SimulateAsync() {
        var sim = Start();
        await foreach (var _ in sim) { }
        return sim.Result;
    }
}
