using Kurrent.Toolkit;
using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Kurrent.Surge.Client;
using Kurrent.Surge.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Codecs.Avro;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Codecs.Protobuf;
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

        // Self-composed, equivalent to the fixture client's own defaults — the pipeline-
        // composition seam on KurrentClient is gone, so the benchmark builds its own
        // resolver/schema/codecs instead of borrowing the client's.
        var catalogs = MessageCatalogDiscovery.GetCatalogs()
            .Where(catalog => catalog.Types.Contains(typeof(RaceCar)))
            .ToList();

        var resolver = MessageResolver.From(catalogs);

        var schema = KurrentClientSchemaOptions.Disabled;

        var codecs = new SchemaCodecProvider([new JsonSchemaCodec(), new ProtobufSchemaCodec(), new AvroSchemaCodec()]);

        var governance = SchemaGovernance.Create(_fixture.Client, catalogs, schema);

        _governed = WriteRecordPipeline.Create(resolver, schema, codecs, governance);

        // Pre-built catalog records, one stream — both paths map the same instances per op.
        var stream = $"governed-append-bench-{RecordCount}";

        _records = new AppendRecord[RecordCount];
        for (var i = 0; i < RecordCount; i++)
            _records[i] = AppendRecord.Create(RaceCar.Generate(), r => r with { Stream = stream });
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
