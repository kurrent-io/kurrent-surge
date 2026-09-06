using Kurrent.Toolkit;
using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// Single-stream append throughput with and without optimistic concurrency.
///
/// Benchmarks:
///   1. append_no_check        — Append records without any precondition check.
///                                UmaDB equivalent: grpc_append_bench (10 events/req, 2-byte payload, 1M pre-seeded).
///   2. append_expected_revision — Append records with optimistic concurrency tracking the revision from each response.
///                                UmaDB equivalent: grpc_append_cond_bench (10 events/req, 2-byte payload, 1M pre-seeded).
///
/// The exact UmaDB match is RecordCount=10, PayloadSize=2.
/// Additional parameter values provide extra Kurrent-specific coverage.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Append")]
public class AppendStreamBenchmarks {
    readonly Random _rng = new(42);

    ClientBenchmarkFixture _fixture = null!;
    StreamName _stream;
    AppendRecord[] _records = null!;
    Revision _revision = null!;

    /// <summary>
    /// Number of records per append call. 10 is the UmaDB-equivalent value.
    /// </summary>
    [Params(1, 10, 100)]
    public int RecordCount { get; set; }

    /// <summary>
    /// Payload size in bytes. 2 matches UmaDB; 64 represents a realistic small event.
    /// </summary>
    [Params(2, 64)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        // Pre-seed the stream so appends happen on a non-empty DB.
        _stream = await _fixture.SeedStream(1);

        // Pre-allocate payload and records — only the gRPC call is measured.
        var payload = new byte[PayloadSize];
        _rng.NextBytes(payload);

        _records = new AppendRecord[RecordCount];

        for (var i = 0; i < RecordCount; i++) {
            _records[i] = AppendRecord.Create(payload, r => {
                r.Stream     = _stream;
                r.DataFormat = SchemaDataFormat.Bytes;
                r.SchemaName = "raw-bytes";
            });
        }

        // Seed the initial revision for the expected-revision benchmark.
        var response = await _fixture.Client.Streams
            .AppendStream(_stream, _records)
            .ConfigureAwait(false);

        _revision = response.Revision;
    }

    /// <summary>
    /// Append records without any precondition check.
    /// UmaDB equivalent: grpc_append_bench (10 events/req, 2-byte payload, 1M pre-seeded, one stream, no condition).
    /// </summary>
    [Benchmark(Baseline = true, Description = "Append (no check)")]
    public async Task<AppendStreamResponse> append_no_check() =>
        await _fixture.Client.Streams
            .AppendStream(_stream, _records)
            .ConfigureAwait(false);

    /// <summary>
    /// Append records with optimistic concurrency, tracking revision from each response.
    /// UmaDB equivalent: grpc_append_cond_bench (10 events/req, 2-byte payload, 1M pre-seeded, one stream, with condition).
    /// </summary>
    [Benchmark(Description = "Append (expected revision)")]
    public async Task<AppendStreamResponse> append_expected_revision() {
        var response = await _fixture.Client.Streams
            .AppendStream(_stream, _records, _revision)
            .ConfigureAwait(false);

        _revision = response.Revision;

        return response;
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
