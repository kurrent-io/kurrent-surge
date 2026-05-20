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
        var builder = ReadStreamRequest.New().Stream(_stream);

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadStream(options).ConfigureAwait(false))
            count++;

        return count;
    }

    [Benchmark(Description = "Read stream with LINQ")]
    public async Task<int> read_stream_with_linq() {
        var builder = ReadStreamRequest.New().Stream(_stream);

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();

        return await _fixture.Client.Streams
            .ReadStream(options)
            .Where(m => m.IsRecord)
            .CountAsync()
            .ConfigureAwait(false);
    }

    // [Benchmark(Description = "Read stream from $all")]
    public async Task<int> read_stream_from_log() {
        var builder = ReadRecordsRequest.New()
            .FromPosition(LogPosition.Earliest)
            .Filter(RecordFilter.ByStream(_stream));

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadRecords(options).ConfigureAwait(false))
            count++;

        return count;
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
