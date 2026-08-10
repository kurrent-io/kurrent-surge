using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// Caller-thread cost of the experimental appender's serialize step — the work Append performs
/// before a record enters a lane. No server round trip is measured; the fixture exists only to
/// build the client the mapping context comes from.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Appender")]
public class AppenderSerializeBenchmarks {
    readonly Random _rng = new(42);

    ClientBenchmarkFixture _fixture = null!;
    StreamsClient          _streams = null!;
    AppendRecord           _record  = null!;

    /// <summary>
    /// Payload size in bytes. 64 matches the client append benchmarks; 512 KB probes the
    /// large-record edge below the server's max record size.
    /// </summary>
    [Params(64, 1_024, 8_192, 524_288)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        // The appender builds its own StreamsClient for the same reason: the mapping context
        // is concrete-client internals, not part of IKurrentClient.Streams.
        _streams = new StreamsClient(_fixture.Client);

        var payload = new byte[PayloadSize];
        _rng.NextBytes(payload);

        _record = AppendRecord.Create(payload, r => {
            r.Stream     = "appender-serialize-bench";
            r.DataFormat = SchemaDataFormat.Bytes;
        });
    }

    /// <summary>
    /// One Append call's caller-thread work: serialize the record to its wire contract.
    /// Mirrors Appender.Append up to the lane enqueue.
    /// </summary>
    [Benchmark(Description = "ToContract (caller-thread serialize)")]
    public async ValueTask<object> serialize() =>
        await _record.ToContract(_fixture.Client.SerializerProvider, _streams.RegistryPolicy, CancellationToken.None);

    [GlobalCleanup]
    public async Task Cleanup() => await _fixture.As<IAsyncDisposable>().DisposeAsync();
}
