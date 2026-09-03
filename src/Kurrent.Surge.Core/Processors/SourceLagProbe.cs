using Kurrent.Toolkit;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Processors.Configuration;

namespace Kurrent.Surge.Processors;

/// <summary>
/// Periodically reads the readable log head and reports how far this drone is behind it, via the
/// <c>source_lag_bytes</c> / <c>source_lag_seconds</c> gauges. Owned by a drone and active only when
/// <see cref="ProcessorOptions.TrackSourceLag"/> is set.
/// </summary>
/// <remarks>
/// The periodic head read is the only signal a gRPC client gets that the head moved ahead — there is
/// no server-pushed "fell behind" notification. The read is always unfiltered: a filtered backward
/// read would scan the entire log for a match. For a filtered subscription this reads as "log left to
/// scan", which is the correct health signal regardless.
/// </remarks>
sealed class SourceLagProbe : IAsyncDisposable {
    readonly Func<CancellationToken, ValueTask<Record>> _readHead;
    readonly Func<DroneProgress>                        _snapshot;
    readonly Action<long, double>                       _onSample;
    readonly AsyncPeriodicTimer                         _timer;

    public SourceLagProbe(
        Func<CancellationToken, ValueTask<Record>> readHead,
        Func<DroneProgress> snapshot,
        Action<long, double> onSample,
        TimeProvider timeProvider,
        TimeSpan? refreshInterval = null
    ) {
        _readHead = readHead;
        _snapshot = snapshot;
        _onSample = onSample;
        _timer    = new(refreshInterval ?? TimeSpan.FromSeconds(10), timeProvider, Sample);
    }

    public void Start(CancellationToken stoppingToken = default) => _timer.Start(stoppingToken);

    async Task Sample(CancellationToken ct) {
        try {
            var head     = await _readHead(ct).ConfigureAwait(false);
            var progress = _snapshot();

            // The resume point: the least-advanced partition's committed position. Unset until the
            // first checkpoint commits, and there is no meaningful baseline before that.
            var floor = progress.LowestCommittedPosition.LogPosition;

            if (floor == LogPosition.Unset)
                return;

            var headPosition = head.Position.LogPosition;

            // Positions are byte offsets, so the gap is a byte distance. Clamp at the head (the
            // readable head can momentarily sit behind a just-committed position during a race).
            var lagBytes = headPosition > floor
                ? headPosition.Value - floor.Value
                : 0L;

            var lagSeconds = progress.LastProcessedAt is { } lastProcessedAt && head.Timestamp > lastProcessedAt
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

    public ValueTask DisposeAsync() => _timer.DisposeAsync();
}
