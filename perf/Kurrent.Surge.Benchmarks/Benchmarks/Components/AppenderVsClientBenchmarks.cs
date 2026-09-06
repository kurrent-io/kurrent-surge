using Kurrent.Toolkit;
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
/// Prices the appender against hand-batched raw AppendRecords calls over the same workload —
/// and the awaitable AppendAsync style (one TaskCompletionSource and cancellation registration
/// per record) against the callback style. Appender runs its defaults: MaxBatchCount 500,
/// Concurrency 2, unkeyed records. The raw client path is the floor: perfect batching with no
/// component in between.
/// </summary>
[Config(typeof(ComponentBenchmarkConfig))]
[BenchmarkCategory("Appender")]
public class AppenderVsClientBenchmarks {
    const int RecordsPerOp = 1_000;
    const int BatchSize    = 500;

    ClientBenchmarkFixture _fixture  = null!;
    Appender               _appender = null!;
    StreamName             _stream;
    RaceCar[]              _values   = null!;

    int _acked;

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        _stream = (await _fixture.TestData.GenerateSet(1, CancellationToken.None)).Stream;

        // Message generation is Bogus-faker work — hoisted out of the measured path so the
        // numbers price the append pipeline, not the test-data factory.
        _values = Enumerable.Range(0, RecordsPerOp).Select(_ => RaceCar.Generate()).ToArray();

        var options = new AppenderOptions();

        options.Connection(_fixture.ConnectionString, client => {
            client.LoggerFactory = _fixture.LoggerFactory;
            client.Schema        = KurrentClientSchemaOptions.AutoRegister;
            client.Resilience    = KurrentClientResilienceOptions.NoResilience;
            // The appender builds its own client, so it must opt into the Testkit catalog like the
            // fixture client does — without it the wire schema name is empty and the server rejects
            // every batch.
            client.Messages.UseTestkitCatalog();
        });

        _appender = new Appender(options);
    }

    /// <summary>
    /// The floor: the same workload hand-chunked into AppendRecords calls of BatchSize,
    /// awaited sequentially. What the appender would have to beat to be free.
    /// </summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = RecordsPerOp, Description = "raw AppendRecords, hand-batched ×500")]
    public async Task<int> client_append_records() {
        var appended = 0;
        var batch    = new AppendRecord[BatchSize];

        for (var offset = 0; offset < RecordsPerOp; offset += BatchSize) {
            for (var i = 0; i < BatchSize; i++)
                batch[i] = NewRecord(offset + i);

            await _fixture.Client.Streams
                .AppendRecords(batch, CancellationToken.None)
                .ConfigureAwait(false);

            appended += BatchSize;
        }

        return appended;
    }

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "appender, callback style")]
    public async Task<int> appender_callback() {
        _acked = 0;

        for (var i = 0; i < RecordsPerOp; i++)
            await _appender.Append(NewRecord(i), _ => Interlocked.Increment(ref _acked), CancellationToken.None);

        await _appender.FlushAsync(CancellationToken.None);

        return _acked;
    }

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "appender, AppendAsync + WhenAll")]
    public async Task<int> appender_async() {
        var pending = new Task<AppendResult>[RecordsPerOp];

        for (var i = 0; i < RecordsPerOp; i++)
            pending[i] = _appender.AppendAsync(NewRecord(i), CancellationToken.None).AsTask();

        await _appender.FlushAsync(CancellationToken.None);

        var results = await Task.WhenAll(pending);

        return results.Length;
    }

    // Catalog-mapped message, not raw bytes: the wire schema name resolves from the value's
    // type through the catalog, and the server rejects records whose schema name is empty.
    // Fresh AppendRecord per append (new record id); the value itself is pre-generated.
    AppendRecord NewRecord(int index) =>
        AppendRecord.Create(_values[index], r => r.Stream = _stream);

    [GlobalCleanup]
    public async Task Cleanup() {
        await _appender.DisposeAsync();
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
