using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;
using TUnit.Core.Interfaces;

namespace Benchmarks;

/// <summary>
/// End-to-end throughput of the experimental appender: Append → lane → batched AppendRecords
/// RPC → acknowledgment callback. One operation is one record; an invocation appends
/// <see cref="RecordsPerOp"/> records then flushes, so every callback has fired before the
/// measurement closes and no work leaks between invocations.
///
/// MaxBatchCount 1 is the degenerate no-batching configuration — its delta to 500 prices the
/// batching layer. The unkeyed method must be invariant across Concurrency; the keyed methods
/// show lane scaling (uniform keys) and the single-lane upper bound (hot key).
/// </summary>
[Config(typeof(ComponentBenchmarkConfig))]
[BenchmarkCategory("Appender")]
public class AppenderThroughputBenchmarks {
    const int RecordsPerOp = 1_000;
    const int UniformKeys  = 64;

    ClientBenchmarkFixture _fixture = null!;
    Appender               _appender = null!;
    StreamName             _stream;
    string[]               _keys   = null!;
    RaceCar[]              _values = null!;

    int _acked;

    /// <summary>Records per batch before the lane flushes.</summary>
    [Params(1, 500)]
    public int MaxBatchCount { get; set; }

    /// <summary>Keyed flush lanes. Unkeyed records always use the single default lane.</summary>
    [Params(1, 8)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        _stream = (await _fixture.TestData.GenerateSet(1, CancellationToken.None)).Stream;

        _keys = Enumerable.Range(0, UniformKeys).Select(i => $"key-{i}").ToArray();

        // Message generation is Bogus-faker work — hoisted out of the measured path so the
        // numbers price the append pipeline, not the test-data factory.
        _values = Enumerable.Range(0, RecordsPerOp).Select(_ => RaceCar.Generate()).ToArray();

        var options = new AppenderOptions {
            MaxBatchCount = MaxBatchCount,
            Concurrency   = Concurrency
        };

        options.Connection(_fixture.ConnectionString, client => {
            client.LoggerFactory = _fixture.LoggerFactory;
            client.Schema        = KurrentClientSchemaOptions.NoValidation;
            client.Resilience    = KurrentClientResilienceOptions.NoResilience;
            // The appender builds its own client, so it must opt into the Testkit catalog like the
            // fixture client does — without it the wire schema name is empty and the server rejects
            // every batch.
            client.Messages.UseTestkitCatalog();
        });

        _appender = new Appender(options);
    }

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "unkeyed — single default lane")]
    public async Task<int> append_unkeyed() =>
        await AppendAndFlush(static (_, _) => null);

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "keyed uniform — 64 keys across lanes")]
    public async Task<int> append_keyed_uniform() =>
        await AppendAndFlush(static (keys, i) => keys[i % UniformKeys]);

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "keyed hot — one key, one lane")]
    public async Task<int> append_keyed_hot() =>
        await AppendAndFlush(static (keys, _) => keys[0]);

    async Task<int> AppendAndFlush(Func<string[], int, string?> selectKey) {
        _acked = 0;

        for (var i = 0; i < RecordsPerOp; i++) {
            var key = selectKey(_keys, i);

            // Record creation and caller-thread serialization stay inside the measurement on
            // purpose — they are the component's contract, and fresh records avoid appending
            // duplicate record ids. The value is pre-generated and catalog-mapped, not raw
            // bytes: the wire schema name resolves from the value's type, and the server
            // rejects an empty schema name.
            var record = AppendRecord.Create(_values[i], r => {
                r.Stream = _stream;
                r.OrderingKey(key);
            });

            await _appender.Append(record, _ => Interlocked.Increment(ref _acked), CancellationToken.None);
        }

        // FlushAsync returns after every buffered batch is sent AND its callbacks have run,
        // so _acked is final here — the barrier that keeps invocations independent.
        await _appender.FlushAsync(CancellationToken.None);

        return _acked;
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _appender.DisposeAsync();
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
