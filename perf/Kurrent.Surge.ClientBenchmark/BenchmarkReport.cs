using System.Diagnostics;

namespace ClientBenchmark;

/// <summary>
/// Writes the same lines as the upstream Rust and Python client benchmarks so one parser reads all three.
/// </summary>
static class BenchmarkReport {
    const int ProgressInterval = 100;

    public static void Header(string mode, BenchmarkOptions options) {
        Console.WriteLine($"Starting Surge benchmark ({mode}): {options.Events} events, {options.PayloadSize} bytes each");
        Console.WriteLine($"Target stream: {options.Stream}");
        Console.WriteLine($"Connection string: {options.ConnectionString}");
    }

    public static void Progress(int appended, int total) {
        if (appended % ProgressInterval == 0)
            Console.WriteLine($"Appended {appended}/{total} events...");
    }

    public static void Results(BenchmarkResult result) {
        var sorted = (long[])result.LatencyTicks.Clone();
        Array.Sort(sorted);

        Console.WriteLine();
        Console.WriteLine("Benchmark Results:");
        Console.WriteLine($"Total events: {result.Events}");
        Console.WriteLine($"Total time: {result.Total.TotalSeconds:F2} s");
        Console.WriteLine($"Throughput: {result.Events / result.Total.TotalSeconds:F2} events/s");
        Console.WriteLine("Latency:");
        Console.WriteLine($"  Mean:   {Milliseconds(sorted.Average()):F3} ms");
        Console.WriteLine($"  Min:    {Milliseconds(sorted[0]):F3} ms");
        Console.WriteLine($"  Max:    {Milliseconds(sorted[^1]):F3} ms");
        Console.WriteLine($"  P50:    {Milliseconds(Quantile(sorted, 0.50)):F3} ms");
        Console.WriteLine($"  P95:    {Milliseconds(Quantile(sorted, 0.95)):F3} ms");
        Console.WriteLine($"  P99:    {Milliseconds(Quantile(sorted, 0.99)):F3} ms");
    }

    // Matches hdrhistogram's value_at_quantile, which the Rust benchmark reports:
    // the smallest sample that at least the given fraction of all samples is less than or equal to.
    static long Quantile(long[] sorted, double quantile) {
        var rank = (int)Math.Ceiling(quantile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    static double Milliseconds(double ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
