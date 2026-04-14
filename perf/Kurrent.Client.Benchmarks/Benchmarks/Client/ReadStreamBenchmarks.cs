using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Client.Streams;

namespace Benchmarks;

/// <summary>
/// Benchmarks for reading from a single named stream with various configurations.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ReadStreamBenchmarks {
    ClientBenchmarkFixture _fixture = null!;
    StreamName _stream;

    [Params(100, 1000, 5000, 10000)]
    public int MessageCount { get; set; }

    [Params(true, false)]
    public bool SkipDecoding { get; set; }

    [GlobalSetup]
    public void Setup() {
        _fixture = new ClientBenchmarkFixture();
        _fixture.InitializeAsync().GetAwaiter().GetResult();
        _stream = _fixture.SeedStream(MessageCount).GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true, Description = "Read stream")]
    public async Task<int> read_stream() {
        var builder = ReadStream.Name(_stream);

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadStream(options))
            count++;

        return count;
    }

    [Benchmark(Description = "Read stream with LINQ")]
    public async Task<int> read_stream_with_linq() {
        var builder = ReadStream.Name(_stream);

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
        var builder = ReadRecords.FromStart()
            .WithFilter(RecordFilter.ByStream(_stream));

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();
        var count   = 0;

        await foreach (var msg in _fixture.Client.Streams.ReadRecords(options))
            count++;

        return count;
    }

    [GlobalCleanup]
    public void Cleanup() {
        _fixture.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
