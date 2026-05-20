using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Client;
using Kurrent.Client.Streams;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// Benchmarks for reading from $all log with different filter configurations.
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class ReadLogBenchmarks {
    ClientBenchmarkFixture _fixture = null!;

    [Params(100, 1000, 5000, 10000)]
    public int MessageCount { get; set; }

    [Params(true, false)]
    public bool SkipDecoding { get; set; }

    [Params(ReadDirection.Forwards, ReadDirection.Backwards)]
    public ReadDirection Direction { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();
        _ = await _fixture.SeedStream(MessageCount);
    }

    [Benchmark(Description = "Read $all with limit")]
    public async Task<int> read_log() {
        var builder = ReadRecordsRequest.New()
            .FromPosition(Direction == ReadDirection.Forwards ? LogPosition.Earliest : LogPosition.Latest)
            .Limit(MessageCount);

        if (Direction == ReadDirection.Backwards)
            builder.Backwards();

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadRecords(options).ConfigureAwait(false)) {
            count++;
            if (count >= MessageCount)
                break;
        }

        return count;
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
