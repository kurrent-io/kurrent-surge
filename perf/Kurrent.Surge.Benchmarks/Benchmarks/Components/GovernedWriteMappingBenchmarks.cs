using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Google.Protobuf;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema.Governance;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Codecs;
using Kurrent.Surge.Codecs.Json;
using Kurrent.Surge.Codecs.Protobuf;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using TUnit.Core.Interfaces;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Benchmarks;

/// <summary>
/// Prices the governed write chain's mapping cost per payload format. Three formats only: the
/// msgpack rows register standalone catalogs the governed extraction cannot see.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Mapping")]
public class GovernedWriteMappingBenchmarks {
    readonly Random _rng = new(42);

    ClientBenchmarkFixture _fixture  = null!;
    KurrentClient          _client   = null!;
    StreamsClient          _streams  = null!;
    WriteRecordPipeline    _governed = null!;
    AppendRecord           _record   = null!;

    [Params("bytes", "json", "proto")]
    public string Payload { get; set; } = "";

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        // Own client: catalogs freeze at construction, and the shared payload type needs a mapping.
        var catalog = new MessageCatalog("governed-mapping-bench").Map<Contracts.AppendRecordsRequest>();

        _client = KurrentClient.Create(_fixture.ConnectionString, opts => {
            opts.LoggerFactory = _fixture.LoggerFactory;
            opts.Schema        = KurrentClientSchemaOptions.AutoRegister;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.UseCatalog(catalog);
        });

        _streams = new StreamsClient(_client);

        // Self-composed, equivalent to the client's own defaults — the pipeline-composition seam
        // on KurrentClient is gone, so the benchmark builds its own resolver/schema/codecs
        // instead of borrowing the client's. The resolver is scoped to the same standalone catalog
        // this client was constructed with.
        var resolver = MessageResolver.From(catalog);

        var schema = KurrentClientSchemaOptions.Disabled;

        var codecs = new SchemaCodecProvider([new JsonSchemaCodec(), new ProtobufSchemaCodec()]);

        var governance = SchemaGovernance.Create(_client, [catalog], schema);

        _governed = WriteRecordPipeline.Create(resolver, codecs, governance);

        var request = NewAppendRequest();

        _record = Payload switch {
            "json"  => AppendRecord.Create(request, r => r.Stream = "governed-mapping-bench"),

            "proto" => AppendRecord.Create(request, r => {
                r.Stream     = "governed-mapping-bench";
                r.DataFormat = SchemaDataFormat.Protobuf;
            }),

            _ => AppendRecord.Create(RandomBytes(), r => {
                r.Stream     = "governed-mapping-bench";
                r.DataFormat = SchemaDataFormat.Bytes;
                r.SchemaName = "raw-bytes";
            })
        };

        byte[] RandomBytes() {
            var payload = new byte[64];
            _rng.NextBytes(payload);
            return payload;
        }
    }

    // A realistic two-record wire request with deterministic values — Bogus randomness would
    // vary string lengths between runs and pollute the format comparison.
    static Contracts.AppendRecordsRequest NewAppendRequest() {
        var request = new Contracts.AppendRecordsRequest();

        for (var i = 0; i < 2; i++) {
            request.Records.Add(new Contracts.AppendRecord {
                RecordId = $"00000000-0000-0000-0000-0000000000{i:00}",
                Stream   = "governed-mapping-bench",
                Data     = ByteString.CopyFromUtf8("""{"value":1,"name":"calibration"}"""),
                Schema   = new Contracts.SchemaInfo {
                    Id     = $"00000000-0000-0000-0000-0000000001{i:00}",
                    Name   = "calibration-event",
                    Format = (Contracts.SchemaFormat)SchemaDataFormat.Json
                }
            });
        }

        return request;
    }

    [Benchmark(Description = "WriteRecordPipeline mapping cost")]
    public async ValueTask<object> governed_pipeline() =>
        await _governed.Execute(_record, CancellationToken.None);

    [GlobalCleanup]
    public async Task Cleanup() {
        await _client.DisposeAsync();
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
