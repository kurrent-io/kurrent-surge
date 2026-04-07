// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bogus;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Testkit.Datasets.ArcadeRacer;

/// <summary>
/// Unified race simulator. Supports both pre-computed (sync) and streaming (async) modes.
/// Pre-computed methods are called by builders and return data directly.
/// Streaming mode exposes <see cref="RaceEvents"/> for async consumption.
/// </summary>
public sealed class RaceSimulator : IAsyncDisposable {
    readonly ArcadeRace        _race;
    readonly ArcadeRaceOptions _options;

    // Streaming infrastructure — lazily initialized
    Channel<IRaceEvent>?     _channel;
    CancellationTokenSource? _cts;
    Task?                    _backgroundTask;

    internal RaceSimulator(string raceId, ArcadeRaceOptions options, Faker faker) {
        RaceId   = raceId;
        _race    = new ArcadeRace(raceId, faker);
        _options = options;
    }

    /// <summary>Unique identifier for this race simulation.</summary>
    public string RaceId { get; }

    #region Pre-computed (sync)

    /// <summary>
    /// Runs the full race synchronously and returns the complete logbook.
    /// Requires finite laps (TotalLaps > 0).
    /// </summary>
    internal RaceLogbook Simulate() {
        if (_options.TotalLaps == 0)
            throw new InvalidOperationException(
                "Cannot pre-compute an unlimited race. Use WithLaps(...) before calling Simulate().");

        var events = RaceSimulationEngine.Run(_race, _options).ToList();
        events.AddRange(_race.Finish());

        return RaceLogbook.From(events);
    }

    /// <summary>
    /// Generates exactly <paramref name="count"/> race events synchronously.
    /// Uses endless laps internally to guarantee the requested count.
    /// </summary>
    internal IReadOnlyList<IRaceEvent> SimulateEvents(int count) =>
        RaceSimulationEngine.Run(_race, _options with { TotalLaps = 0 }).Take(count).ToList();

    #endregion

    #region Streaming (async)

    /// <summary>
    /// Starts the background producer. Must be called before <see cref="RaceEvents"/>.
    /// </summary>
    internal RaceSimulator StartStreaming() {
        _cts     = new CancellationTokenSource();
        _channel = Channel.CreateBounded<IRaceEvent>(new BoundedChannelOptions(100) {
            SingleWriter = true,
            SingleReader = false,
            FullMode     = BoundedChannelFullMode.Wait
        });

        _backgroundTask = Task.Run(() => ProduceEventsAsync(_cts.Token));
        return this;

        async Task ProduceEventsAsync(CancellationToken ct) {
            try {
                foreach (var e in RaceSimulationEngine.Run(_race, _options with { TotalLaps = 0 })) {
                    ct.ThrowIfCancellationRequested();
                    await _channel!.Writer.WriteAsync(e, ct);

                    if (_options.Pacing != TimeSpan.Zero)
                        await Task.Delay(_options.Pacing, ct);
                }
            }
            catch (OperationCanceledException) {
                // Expected when disposed or cancelled
            }
        }
    }

    /// <summary>
    /// Streams race events as they are produced. When <paramref name="ct"/> is cancelled,
    /// the stream ends. If <paramref name="includeRaceFinished"/> is true, a RaceFinished
    /// event is yielded as the last event before the stream completes.
    /// </summary>
    public async IAsyncEnumerable<IRaceEvent> RaceEvents(
        bool includeRaceFinished = false,
        [EnumeratorCancellation] CancellationToken ct = default) {

        if (_channel is null)
            throw new InvalidOperationException("Streaming not started. Call Start() on the builder first.");

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
            await StopProducer();

            foreach (var e in _race.Finish())
                yield return e;
        }
    }


    async Task StopProducer() {
        if (_cts is null) return;

        await _cts.CancelAsync();

        try { await _backgroundTask!; }
        catch (OperationCanceledException) { }

        _channel!.Writer.TryComplete();
    }

    #endregion

    #region Disposal

    public async ValueTask DisposeAsync() {
        if (_cts is not null) {
            await StopProducer();
            _cts.Dispose();
        }
    }

    #endregion
}
