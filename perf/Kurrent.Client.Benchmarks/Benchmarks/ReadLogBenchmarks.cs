using BenchmarkDotNet.Attributes;
using Kurrent.Client.Benchmarks.Infrastructure;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Benchmarks.Benchmarks;

/// <summary>
/// Benchmarks for reading from $all log with different filter configurations.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class ReadLogBenchmarks {
    BenchmarkFixture _fixture = null!;

    [Params(10000)]
    public int MessageCount { get; set; }

    [Params(1000, 5000)]
    public int BufferCapacity { get; set; }

    [Params(true, false)]
    public bool SkipDecoding { get; set; }

    [Params(ReadDirection.Forwards, ReadDirection.Backwards)]
    public ReadDirection Direction { get; set; }

    [GlobalSetup]
    public void Setup() {
        _fixture = new BenchmarkFixture();
        _fixture.InitializeAsync().GetAwaiter().GetResult();
        _ = _fixture.SeedStream(MessageCount).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Read $all forward with limit")]
    public async Task<int> read_log_forward() {
        var builder = ReadLog.From(Direction == ReadDirection.Forwards ? LogPosition.Earliest : LogPosition.Latest)
            .WithBufferCapacity(BufferCapacity)
            .WithMaxRecords(MessageCount);

        if (Direction == ReadDirection.Backwards)
            builder.Backwards();

        if (SkipDecoding)
            builder.SkipDecoding();

        var options = builder.Build();

        var messages = await _fixture.Client.Streams
            .ReadRecords(options)
            .UnwrapAsync()
            .ConfigureAwait(false);

        try {
            var count = 0;

            await foreach (var msg in messages) {
                count++;
                if (count >= MessageCount)
                    break;
            }

            return count;
        }
        finally {
            await messages.DisposeAsync().ConfigureAwait(false);
        }
    }

    [GlobalCleanup]
    public void Cleanup() {
        _fixture.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
