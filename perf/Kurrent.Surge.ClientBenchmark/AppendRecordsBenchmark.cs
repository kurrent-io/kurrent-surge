using System.Diagnostics;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;

namespace ClientBenchmark;

/// <summary>
/// One record per call, strictly sequential. This is the shape of the upstream Rust client benchmark.
/// </summary>
static class AppendRecordsBenchmark {
    public static async Task<BenchmarkResult> Run(BenchmarkOptions options, CancellationToken ct) {
        await using var client = KurrentClient.Create(options.ConnectionString, options.Configure);

        var        payload   = options.CreatePayload();
        StreamName stream    = options.Stream;
        var        latencies = new long[options.Events];
        var        batch     = new AppendRecord[1];

        var start = Stopwatch.GetTimestamp();

        for (var i = 0; i < options.Events; i++) {
            batch[0] = AppendRecord.Create(payload, BenchmarkOptions.SchemaName, r => r with { Stream = stream });

            var stepStart = Stopwatch.GetTimestamp();
            await client.Streams.AppendRecords(batch, options: null, ct);
            latencies[i] = Stopwatch.GetTimestamp() - stepStart;

            BenchmarkReport.Progress(i + 1, options.Events);
        }

        return new BenchmarkResult(options.Events, Stopwatch.GetElapsedTime(start), latencies);
    }
}
