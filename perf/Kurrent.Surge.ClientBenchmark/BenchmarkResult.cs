namespace ClientBenchmark;

sealed record BenchmarkResult(int Events, TimeSpan Total, long[] LatencyTicks);
