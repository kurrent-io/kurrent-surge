using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Google.Protobuf;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using TUnit.Core.Interfaces;

using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Benchmarks;

/// <summary>
/// Prices the governed write chain against the split pipeline and the ToContract baseline on
/// the identical record. The governed chain decomposes resolution into discrete steps AND
/// never mints the per-record SchemaInfo.Id fallback the other two shapes still pay — so its
/// delta is step tax minus one Guid.NewGuid().ToString() per record. Three formats only: the
/// msgpack rows register standalone catalogs the governed extraction cannot see.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Mapping")]
public class GovernedWriteMappingBenchmarks {
    readonly Random _rng = new(42);

    ClientBenchmarkFixture      _fixture  = null!;
    KurrentClient               _client   = null!;
    StreamsClient               _streams  = null!;
    WriteRecordPipeline         _split    = null!;
    GovernedWriteRecordPipeline _governed = null!;
    AppendRecord                _record   = null!;

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
            opts.Schema        = KurrentClientSchemaOptions.NoValidation;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.UseCatalog(catalog);
        });

        _streams = new StreamsClient(_client);

        var mapping = _streams.CreateMappingContext(CancellationToken.None);
        _split = WriteRecordPipeline.Split(mapping.SerializerProvider, mapping.Policy);

        var (manager, _, registrations) = SchemaManagerGovernanceAdapter.Extract(mapping.SerializerProvider);

        var resolved = mapping.Policy
            .Resolve(registrations[SchemaDataFormat.Json], null, SchemaDataFormat.Json)
            .EnsureDataFormatCompliance(SchemaDataFormat.Json);

        var validation = new CatalogValidationInterceptor(new SchemaManagerGovernanceAdapter(manager), [catalog], resolved);

        _governed = GovernedWriteRecordPipeline.From(mapping.SerializerProvider, mapping.Policy, validation);

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

    [Benchmark(Baseline = true, Description = "ToContract mapping")]
    public async ValueTask<object> to_contract() =>
        await _record.ToContract(_streams.CreateMappingContext(CancellationToken.None));

    [Benchmark(Description = "WriteRecordPipeline.Split (resolve + encode)")]
    public async ValueTask<object> split_pipeline() =>
        await _split.Execute(_record, CancellationToken.None);

    [Benchmark(Description = "GovernedWriteRecordPipeline (governed steps, no id fallback)")]
    public async ValueTask<object> governed_pipeline() =>
        await _governed.Execute(_record, CancellationToken.None);

    [GlobalCleanup]
    public async Task Cleanup() {
        await _client.DisposeAsync();
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
