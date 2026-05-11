using BenchmarkDotNet.Attributes;
using Kurrent.Streaming.Partitioning.Hashing;

namespace Benchmarks;

[MemoryDiagnoser(displayGenColumns: false), Config(typeof(BenchmarkConfig))]
public class XXHashBenchmarks {
    byte[] _buffer = null!;

    // Partition-key realistic range (the actual workload for this hash):
    //   8    — short numeric/UUID fragment (below Hash32 stripe — fast path)
    //   16   — one Hash32 stripe / short stream ID
    //   24   — e.g. "customer-12345"
    //   32   — typical stream ID like "orders-abc123def456"
    //   64   — longer composite key
    //   128  — long stream ID
    //   256  — upper bound of realistic keys
    // Reference sizes (not typical workload):
    //   1_024     — medium buffer
    //   1_048_576 — 1 MB, cache-pressure range
    [Params(8, 16, 24, 32, 64, 128, 256, 1_024, 1_048_576)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup() {
        _buffer = new byte[BufferSize];
        new Random(42).NextBytes(_buffer);
    }

    [Benchmark]
    public uint Hash32() => XxHash.Hash32(_buffer);
}
