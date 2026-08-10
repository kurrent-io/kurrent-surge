using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Testkit.Datasets.ArcadeRacer.Fakers;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;
using TUnit.Core.Interfaces;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Benchmarks;

/// <summary>
/// End-to-end append against the live server: the production client path (ToContract mapping
/// inside AppendRecords) versus the governed pipeline framing the same records and sending
/// through the raw V2 service call. Both sides map the full batch and pay one RPC per op, so
/// the within-run ratio isolates the mapping difference — including the SchemaInfo.Id fallback
/// the production path still mints per record.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Append")]
public class GovernedAppendBenchmarks {
    ClientBenchmarkFixture _fixture  = null!;
    StreamsClient          _streams  = null!;
    WriteRecordPipeline    _governed = null!;
    AppendRecord[]         _records  = null!;

    /// <summary>Records per append call — the mapping delta scales with the batch.</summary>
    [Params(10, 100)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        _streams = new StreamsClient(_fixture.Client);

        var policy = _streams.RegistryPolicy;

        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(typeof(RaceCar)))
            .ToList();

        var resolved = policy
            .Resolve(_fixture.Client.Registrations[SchemaDataFormat.Json], null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        var validation = new CatalogValidationInterceptor(SchemaGovernance.Create(_fixture.Client), catalogs, resolved);

        _governed = WriteRecordPipeline.Create(
            _fixture.Client.MessageResolver, policy, _fixture.Client.Registrations, _fixture.Client.Codecs, validation);

        // Pre-built catalog records, one stream — both paths map the same instances per op.
        var stream = $"governed-append-bench-{RecordCount}";

        _records = new AppendRecord[RecordCount];
        for (var i = 0; i < RecordCount; i++)
            _records[i] = AppendRecord.Create(RaceCar.Generate(), r => r.Stream = stream);
    }

    [Benchmark(Baseline = true, Description = "Client AppendRecords (ToContract mapping)")]
    public async Task<AppendRecordsResponse> client_append() =>
        await _fixture.Client.Streams
            .AppendRecords(_records, CancellationToken.None)
            .ConfigureAwait(false);

    [Benchmark(Description = "Governed pipeline + raw V2 append")]
    public async Task<object> governed_append() {
        var request = new Contracts.AppendRecordsRequest();

        foreach (var record in _records)
            request.Records.Add(await _governed.Execute(record, CancellationToken.None).ConfigureAwait(false));

        return await _streams.ServiceClient
            .AppendRecordsAsync(request, cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
