using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Client;
using Kurrent.Client.Streams;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// Benchmarks for reading from a single named stream with various configurations.
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class ReadStreamBenchmarks {
    ClientBenchmarkFixture _fixture = null!;
    StreamName _stream;

    [Params(100, 1000, 5000, 10000)]
    public int MessageCount { get; set; }

    [Params(true, false)]
    public bool SkipDecoding { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();
        _stream = await _fixture.SeedStream(MessageCount);
    }

    [Benchmark(Baseline = true, Description = "Read stream")]
    public async Task<int> read_stream() {
        var options = new ReadStreamOptions { SkipDecoding = SkipDecoding };
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadStream(_stream, options).ConfigureAwait(false))
            count++;

        return count;
    }

    [Benchmark(Description = "Read stream with LINQ")]
    public async Task<int> read_stream_with_linq() {
        var options = new ReadStreamOptions { SkipDecoding = SkipDecoding };

        return await _fixture.Client.Streams
            .ReadStream(_stream, options)
            .Where(m => m.IsRecord)
            .CountAsync()
            .ConfigureAwait(false);
    }

    // [Benchmark(Description = "Read stream from $all")]
    public async Task<int> read_stream_from_log() {
        var options = new ReadRecordsOptions {
            Start        = LogPosition.Earliest,
            Filter       = RecordFilter.ByStream(_stream),
            SkipDecoding = SkipDecoding
        };
        var count = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadRecords(options).ConfigureAwait(false))
            count++;

        return count;
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
