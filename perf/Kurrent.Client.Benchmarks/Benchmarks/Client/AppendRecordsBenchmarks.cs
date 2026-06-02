using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Client;
using Kurrent.Client.Streams;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// Multi-stream atomic append throughput via the V2 AppendRecords API.
///
/// Benchmarks:
///   3. append_multi_stream              — Atomic append across multiple streams without consistency checks.
///                                         No direct UmaDB equivalent (UmaDB supports multi-tag but doesn't benchmark it).
///   4. append_multi_stream_with_checks  — Same as above with ConsistencyCheck.StreamExists per stream.
///                                         No direct UmaDB equivalent; Kurrent-specific coverage.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Append")]
public class AppendRecordsBenchmarks {
    readonly Random _rng = new(42);

    ClientBenchmarkFixture _fixture = null!;
    AppendRecord[] _records = null!;
    AppendRecordsOptions _checks = null!;

    /// <summary>
    /// Number of records per stream in each append call.
    /// </summary>
    [Params(1, 10, 100)]
    public int RecordsPerStream { get; set; }

    /// <summary>
    /// Number of distinct streams targeted by a single atomic append.
    /// </summary>
    [Params(2, 5, 10)]
    public int StreamCount { get; set; }

    /// <summary>
    /// Payload size in bytes. 2 matches UmaDB convention; 64 represents a realistic small event.
    /// </summary>
    [Params(2, 64)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        // Seed distinct streams — can't use the caching SeedStream() since
        // multiple calls with the same count would return the same stream.
        var streams = await _fixture.SeedStreams(StreamCount, 1);

        // Pre-allocate payload and records — only the gRPC call is measured.
        var payload = new byte[PayloadSize];
        _rng.NextBytes(payload);

        var totalRecords = RecordsPerStream * StreamCount;
        var records = new AppendRecord[totalRecords];

        var idx = 0;

        for (var s = 0; s < StreamCount; s++) {
            for (var r = 0; r < RecordsPerStream; r++) {
                records[idx++] = AppendRecord.New()
                    .Stream(streams[s])
                    .Value(payload)
                    .DataFormat(SchemaDataFormat.Bytes)
                    .Build();
            }
        }

        // Pre-build records and checks — only the gRPC call is measured.
        _records = records;

        _checks = new AppendRecordsOptions {
            Checks = streams.Select(s => (ConsistencyCheck)ConsistencyCheck.StreamExists(s)).ToArray()
        };
    }

    /// <summary>
    /// Atomic append across multiple streams without consistency checks.
    /// No direct UmaDB equivalent — Kurrent-specific multi-stream coverage.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Multi-stream append (no checks)")]
    public async Task<AppendRecordsResponse> append_multi_stream() =>
        await _fixture.Client.Streams
            .AppendRecords(_records, CancellationToken.None)
            .ConfigureAwait(false);

    /// <summary>
    /// Atomic append across multiple streams with ConsistencyCheck.StreamExists per stream.
    /// No direct UmaDB equivalent — Kurrent-specific consistency-check coverage.
    /// </summary>
    [Benchmark(Description = "Multi-stream append (stream-exists checks)")]
    public async Task<AppendRecordsResponse> append_multi_stream_with_checks() =>
        await _fixture.Client.Streams
            .AppendRecords(_records, _checks, CancellationToken.None)
            .ConfigureAwait(false);

    [GlobalCleanup]
    public async Task Cleanup() {
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
