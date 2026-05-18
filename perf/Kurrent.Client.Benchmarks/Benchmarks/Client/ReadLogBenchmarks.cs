using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Client;
using Kurrent.Client.Streams;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// Benchmarks for reading from $all log with different filter configurations.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ReadLogBenchmarks {
    ClientBenchmarkFixture _fixture = null!;

    [Params(10000)]
    public int MessageCount { get; set; }

    [Params(true, false)]
    public bool SkipDecoding { get; set; }

    [Params(ReadDirection.Forwards, ReadDirection.Backwards)]
    public ReadDirection Direction { get; set; }

    [GlobalSetup]
    public void Setup() {
        _fixture = new ClientBenchmarkFixture();
        _fixture.As<IAsyncInitializer>().InitializeAsync().GetAwaiter().GetResult();
        _ = _fixture.SeedStream(MessageCount).GetAwaiter().GetResult();
    }

    // TODO SS: wrong name or we need proper benchmark for reading with limit, forwards and backwards.
    [Benchmark(Description = "Read $all forward with limit")]
    public async Task<int> read_log_forward() {
        var builder = ReadRecordsRequest.New()
            .FromPosition(Direction == ReadDirection.Forwards ? LogPosition.Earliest : LogPosition.Latest)
            .Limit(MessageCount);

        if (Direction == ReadDirection.Backwards)
            builder.Backwards();

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadRecords(options)) {
            count++;
            if (count >= MessageCount)
                break;
        }

        return count;
    }

    [GlobalCleanup]
    public void Cleanup() {
        _fixture.As<IAsyncDisposable>().DisposeAsync()
            .AsTask().GetAwaiter().GetResult();
    }
}
