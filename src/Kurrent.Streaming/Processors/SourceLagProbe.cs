using Kurrent.Client.Streams;

namespace Kurrent.Streaming;

/// <summary>
/// Periodically reads the readable log head and reports how far this drone is behind it, via the
/// <c>source_lag_bytes</c> / <c>source_lag_seconds</c> gauges. Owned by a drone and active only when
/// <see cref="Options.ProcessorOptions.TrackSourceLag"/> is set.
/// </summary>
/// <remarks>
/// The periodic head read is the only signal a gRPC client gets that the head moved ahead — there is
/// no server-pushed "fell behind" notification. The read is always unfiltered: a filtered backward
/// read would scan the entire log for a match. For a filtered subscription this reads as "log left to
/// scan", which is the correct health signal regardless.
/// </remarks>
sealed class SourceLagProbe : IAsyncDisposable {
    // Fixed cadence — deliberately not user-configurable. One extra unfiltered head read per tick.
    static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    readonly Func<CancellationToken, ValueTask<Record>> _readHead;
    readonly Func<DroneProgress>                        _snapshot;
    readonly Action<long, double>                       _onSample;
    readonly AsyncPeriodicTimer                         _timer;

    public SourceLagProbe(
        Func<CancellationToken, ValueTask<Record>> readHead,
        Func<DroneProgress> snapshot,
        Action<long, double> onSample,
        TimeProvider timeProvider
    ) {
        _readHead = readHead;
        _snapshot = snapshot;
        _onSample = onSample;
        _timer    = new(RefreshInterval, timeProvider, Sample);
    }

    public void Start(CancellationToken stoppingToken = default) => _timer.Start(stoppingToken);

    async Task Sample(CancellationToken ct) {
        try {
            var head     = await _readHead(ct).ConfigureAwait(false);
            var progress = _snapshot();

            // Furthest-behind partition: lowest committed (or, lacking a commit, tracked) position.
            if (LowestProgressed(progress) is not { } floor)
                return; // no progress yet — no meaningful baseline to measure against

            var headPosition = head.Position.LogPosition;

            // Positions are byte offsets, so the gap is a byte distance. Clamp at the head (the
            // readable head can momentarily sit behind a just-committed position during a race).
            var lagBytes = headPosition > floor
                ? headPosition.Value - floor.Value
                : 0L;

            var lagSeconds = progress.Throughput.LastProcessedAt is { } lastProcessedAt && head.Timestamp > lastProcessedAt
                ? (head.Timestamp - lastProcessedAt).TotalSeconds
                : 0d;

            _onSample(lagBytes, lagSeconds);
        }
        catch (OperationCanceledException) {
            // Shutting down — stop sampling.
        }
        catch {
            // Best-effort observability: a transient head-read failure skips this tick; the gauge
            // keeps its last value and the next refresh retries.
        }
    }

    // Lowest position across owned partitions — committed if any, else tracked. Skips partitions with
    // no progress. Phrased across partitions so it stays correct when partitioning lands.
    static LogPosition? LowestProgressed(DroneProgress progress) {
        LogPosition? lowest = null;

        foreach (var (_, partition) in progress.Partitions) {
            var position = partition.LastFlushedPosition.LogPosition != LogPosition.Unset
                ? partition.LastFlushedPosition.LogPosition
                : partition.LastTrackedPosition.LogPosition;

            if (position == LogPosition.Unset)
                continue;

            if (lowest is null || position < lowest.Value)
                lowest = position;
        }

        return lowest;
    }

    public ValueTask DisposeAsync() => _timer.DisposeAsync();
}
