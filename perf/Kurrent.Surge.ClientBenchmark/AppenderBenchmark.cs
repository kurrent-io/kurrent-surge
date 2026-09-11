using System.Diagnostics;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;

namespace ClientBenchmark;

/// <summary>
/// Pipelined appends through the experimental <see cref="Appender"/>. The appender batches records,
/// so per-record latency is measured from submission to server acknowledgement.
/// </summary>
static class AppenderBenchmark {
    public sealed record Settings(int MaxBatchCount, TimeSpan LingerTimeout);

    public static async Task<BenchmarkResult> Run(BenchmarkOptions options, Settings settings, CancellationToken ct) {
        var appenderOptions = new AppenderOptions {
            MaxBatchCount = settings.MaxBatchCount,
            LingerTimeout = settings.LingerTimeout,
            OnError       = error => Console.Error.WriteLine($"Appender error: {error}")
        };

        appenderOptions.Connection(options.ConnectionString, options.Configure);

        await using var appender = new Appender(appenderOptions);

        var        payload   = options.CreatePayload();
        StreamName stream    = options.Stream;
        var        latencies = new long[options.Events];

        var        acked      = 0;
        var        failed     = 0;
        Exception? firstError = null;

        var start = Stopwatch.GetTimestamp();

        for (var i = 0; i < options.Events; i++) {
            var record    = AppendRecord.Create(payload, BenchmarkOptions.SchemaName, r => r with { Stream = stream });
            var index     = i;
            var submitted = Stopwatch.GetTimestamp();

            // Append returns once the record is enqueued, so the appender's backpressure throttles this
            // loop. The callback runs when the server acknowledges the batch that carries the record.
            await appender.Append(record, result => {
                latencies[index] = Stopwatch.GetTimestamp() - submitted;

                if (!result.IsSuccess) {
                    Interlocked.Increment(ref failed);
                    Interlocked.CompareExchange(ref firstError, result.Error, null);
                }

                BenchmarkReport.Progress(Interlocked.Increment(ref acked), options.Events);
            }, ct);
        }

        await appender.FlushAsync(ct);

        var total = Stopwatch.GetElapsedTime(start);

        if (failed > 0)
            throw new InvalidOperationException($"{failed} of {options.Events} appends were not acknowledged.", firstError);

        return new BenchmarkResult(options.Events, total, latencies);
    }
}
