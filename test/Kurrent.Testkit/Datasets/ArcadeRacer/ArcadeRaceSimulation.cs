// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer;

/// <summary>Static entry point for creating race simulations.</summary>
public static class ArcadeRaceSimulator {
    /// <summary>Creates a streaming race simulation builder. Always endless.</summary>
    public static StreamingRaceSimulatorBuilder NewStreamingRace(Faker? faker = null) => new(faker);

    /// <summary>Creates a pre-computed race simulation builder.</summary>
    public static PrecomputedRaceSimulatorBuilder NewRace(Faker? faker = null) => new(faker);
}

/// <summary>
/// Shared simulation engine. Single source of truth for the race loop.
/// </summary>
static class RaceSimulationEngine {
    /// <summary>
    /// Lazily generates race events. Consumers drive the pace.
    /// </summary>
    public static IEnumerable<IRaceEvent> Run(ArcadeRace race, ArcadeRaceOptions options) {
        foreach (var e in race.FormGrid(options)) yield return e;
        foreach (var e in race.Start()) yield return e;

        while (race.State.IsActive && (options.TotalLaps == 0 || race.State.Lap < options.TotalLaps)) {
            foreach (var e in race.SimulateLap()) yield return e;
            foreach (var e in race.CheckWeather()) yield return e;
            foreach (var e in race.CheckIncident()) yield return e;
        }
    }
}

/// <summary>
/// Live streaming race simulator backed by a bounded channel and a background task.
/// Always endless — runs until cancelled or disposed.
/// </summary>
public sealed class StreamingRaceSimulator : IAsyncDisposable {
    readonly ArcadeRace              _race;
    readonly ArcadeRaceOptions       _options;
    readonly Channel<IRaceEvent>     _channel;
    readonly CancellationTokenSource _cts = new();
    readonly Task                    _backgroundTask;

    internal StreamingRaceSimulator(string raceId, ArcadeRaceOptions options, Faker faker) {
        RaceId   = raceId;
        _race    = new ArcadeRace(raceId, faker);
        _options = options with { TotalLaps = 0 };

        _channel = Channel.CreateBounded<IRaceEvent>(new BoundedChannelOptions(100) {
            SingleWriter = true,
            SingleReader = false,
            FullMode     = BoundedChannelFullMode.Wait
        });

        _backgroundTask = Task.Run(() => ProduceEventsAsync(_cts.Token));
    }

    /// <summary>Unique identifier for this race simulation.</summary>
    public string RaceId { get; }

    /// <summary>
    /// Streams race events as they are produced. When <paramref name="ct"/> is cancelled,
    /// the stream ends. If <paramref name="includeRaceFinished"/> is true, a RaceFinished
    /// event is yielded as the last event before the stream completes.
    /// </summary>
    public async IAsyncEnumerable<IRaceEvent> RaceEvents(
        bool includeRaceFinished = false,
        [EnumeratorCancellation] CancellationToken ct = default) {

        var cancelled = false;

        await using var enumerator = _channel.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);

        while (true) {
            bool moved;

            try { moved = await enumerator.MoveNextAsync(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                cancelled = true;
                break;
            }

            if (!moved) break;

            yield return enumerator.Current;
        }

        if (cancelled && includeRaceFinished) {
            await _cts.CancelAsync();

            try { await _backgroundTask; }
            catch (OperationCanceledException) { }

            _channel.Writer.TryComplete();

            foreach (var e in _race.Finish())
                yield return e;
        }
    }

    #region Background Producer

    async Task ProduceEventsAsync(CancellationToken ct) {
        try {
            foreach (var e in RaceSimulationEngine.Run(_race, _options)) {
                ct.ThrowIfCancellationRequested();
                await _channel.Writer.WriteAsync(e, ct);

                if (_options.Pacing != TimeSpan.Zero)
                    await Task.Delay(_options.Pacing, ct);
            }
        }
        catch (OperationCanceledException) {
            // Expected when disposed or RaceEvents cancellation stops the producer
        }
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync() {
        await _cts.CancelAsync();

        try { await _backgroundTask; }
        catch (OperationCanceledException) { }

        _channel.Writer.TryComplete();
        _cts.Dispose();
    }

    #endregion
}

/// <summary>
/// Fluent builder for streaming race simulations.
/// Streaming races are always endless — no lap configuration.
/// </summary>
public sealed class StreamingRaceSimulatorBuilder {
    readonly ArcadeRacerDataSet _ds;

    internal StreamingRaceSimulatorBuilder(Faker? faker = null) {
        Faker  = faker ?? new Faker();
        _ds    = new ArcadeRacerDataSet(Faker);
        RaceId = TestId.New();

        Options = new ArcadeRaceOptions {
            Cars       = _ds.RaceCar.Generate(Faker.Random.Int(3, 9)),
            Circuit    = _ds.Circuit.Generate(),
            Conditions = new RaceConditionsFaker(Faker).Generate(),
            TotalLaps  = 0,
            Pacing     = TimeSpan.Zero
        };
    }

    public string  RaceId  { get; }
    public Faker Faker   { get; }

    ArcadeRaceOptions Options { get; set; }

    #region Configuration

    public StreamingRaceSimulatorBuilder WithCars(int count) {
        Options = Options with { Cars = _ds.RaceCar.Generate(count) };
        return this;
    }

    public StreamingRaceSimulatorBuilder WithCars(Func<Faker, int> cars) => WithCars(cars(Faker));

    public StreamingRaceSimulatorBuilder WithDifficulty(CircuitDifficulty difficulty) {
        Options = Options with {
            Circuit = Circuit.New(Faker).RuleFor(x => x.Difficulty, difficulty).Generate()
        };
        return this;
    }

    public StreamingRaceSimulatorBuilder WithConditions(TimeOfDay timeOfDay, Weather weather) {
        Options = Options with { Conditions = new RaceConditions { TimeOfDay = timeOfDay, Weather = weather } };
        return this;
    }

    /// <summary>Sets the delay between events in streaming mode.</summary>
    public StreamingRaceSimulatorBuilder WithPacing(TimeSpan pacing) {
        Options = Options with { Pacing = pacing };
        return this;
    }

    #endregion

    /// <summary>Starts the race simulation on a background task and returns a live streaming simulator.</summary>
    public StreamingRaceSimulator Start() => new(RaceId, Options, Faker);
}

/// <summary>
/// Pre-computed race simulator. Runs the simulation synchronously and returns results immediately.
/// No channels, no background tasks, no async.
/// </summary>
public sealed class PrecomputedRaceSimulator {
    readonly ArcadeRace        _race;
    readonly ArcadeRaceOptions _options;

    internal PrecomputedRaceSimulator(string raceId, ArcadeRaceOptions options, Faker faker) {
        RaceId   = raceId;
        _race    = new ArcadeRace(raceId, faker);
        _options = options;
    }

    public string RaceId { get; }

    /// <summary>
    /// Runs the full race synchronously and returns the complete logbook.
    /// Requires finite laps (TotalLaps > 0).
    /// </summary>
    public RaceLogbook Simulate() {
        if (_options.TotalLaps == 0)
            throw new InvalidOperationException(
                "Cannot pre-compute an unlimited race. Use WithLaps(...) before calling Simulate().");

        var events = RaceSimulationEngine.Run(_race, _options).ToList();
        events.AddRange(_race.Finish());

        return RaceLogbook.From(events);
    }

    // /// <summary>
    // /// Generates exactly <paramref name="count"/> race events synchronously.
    // /// Uses endless laps internally to guarantee the requested count.
    // /// </summary>
    // public IReadOnlyList<IRaceEvent> SimulateEvents(int count) =>
    //     RaceSimulationEngine.Run(_race, _options with { TotalLaps = 0 }).Take(count).ToList();
}

/// <summary>
/// Pre-computed race simulator builder. Supports finite laps.
/// </summary>
public sealed class PrecomputedRaceSimulatorBuilder {
    readonly ArcadeRacerDataSet _ds;

    internal PrecomputedRaceSimulatorBuilder(Faker? faker = null) {
        Faker  = faker ?? new Faker();
        _ds    = new ArcadeRacerDataSet(Faker);
        RaceId = TestId.New();

        Options = new ArcadeRaceOptions {
            Cars       = _ds.RaceCar.Generate(Faker.Random.Int(3, 9)),
            Circuit    = _ds.Circuit.Generate(),
            Conditions = new RaceConditionsFaker(Faker).Generate(),
            TotalLaps  = Faker.Random.Int(3, 12),
            Pacing     = TimeSpan.Zero
        };
    }

    public string  RaceId  { get; }
    public Faker Faker   { get; }

    ArcadeRaceOptions Options { get; set; }

    #region Configuration

    public PrecomputedRaceSimulatorBuilder WithCars(int count) {
        Options = Options with { Cars = _ds.RaceCar.Generate(count) };
        return this;
    }

    public PrecomputedRaceSimulatorBuilder WithCars(Func<Faker, int> cars) => WithCars(cars(Faker));

    public PrecomputedRaceSimulatorBuilder WithLaps(int laps) {
        Options = Options with { TotalLaps = laps };
        return this;
    }

    public PrecomputedRaceSimulatorBuilder WithLaps(Func<Faker, int> laps) => WithLaps(laps(Faker));

    public PrecomputedRaceSimulatorBuilder WithDifficulty(CircuitDifficulty difficulty) {
        Options = Options with {
            Circuit = Circuit.New(Faker).RuleFor(x => x.Difficulty, difficulty).Generate()
        };
        return this;
    }

    public PrecomputedRaceSimulatorBuilder WithConditions(TimeOfDay timeOfDay, Weather weather) {
        Options = Options with { Conditions = new RaceConditions { TimeOfDay = timeOfDay, Weather = weather } };
        return this;
    }

    #endregion

    /// <summary>Creates the pre-computed simulator.</summary>
    public PrecomputedRaceSimulator Build() => new(RaceId, Options, Faker);

    /// <summary>Runs the full race synchronously and returns the complete logbook.</summary>
    public RaceLogbook Simulate() => Build().Simulate();

    // /// <summary>Generates exactly <paramref name="count"/> race events synchronously.</summary>
    // public IReadOnlyList<IRaceEvent> SimulateEvents(int count) {
    //     Options = Options with { TotalLaps = 0 };
    //     return Build().SimulateEvents(count);
    // }
}
