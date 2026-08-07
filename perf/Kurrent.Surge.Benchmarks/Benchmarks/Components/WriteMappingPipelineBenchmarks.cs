extern alias MsgPackOfficial;
extern alias MsgPackNerdbank;

using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using Google.Protobuf;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using TUnit.Core.Interfaces;

using Contracts      = KurrentDB.Protocol.V2.Streams;
using OfficialMsgPack = MsgPackOfficial::Kurrent.Surge.Client.Schema.Serialization.MessagePack;
using NerdbankMsgPack = MsgPackNerdbank::Kurrent.Surge.Client.Schema.Serialization.MessagePack;

namespace Benchmarks;

/// <summary>
/// Prices the composed WriteRecordPipeline shapes against the ToContract mapping on the
/// identical record, across the payload formats. The fused pipeline shares ToContract's
/// seams, so its delta is the step machinery; the split pipeline resolves before encoding with
/// a per-type schema cache, so its delta is the fusion undone. The format axis calibrates the
/// mapping step against industry serializer tiers on the SAME payload object — a real
/// two-record <c>AppendRecordsRequest</c> wire contract — so json vs proto measures the format,
/// not the object shape. bytes remains the envelope floor with no payload work at all. msgpack
/// and msgpack-nerdbank can't reuse that object — neither library can generate a formatter for a
/// protobuf-generated type without a hand-written one — so they encode a same-shape POCO mirror
/// instead (see <see cref="CalibrationMsgPackRequest"/>).
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Mapping")]
public class WriteMappingPipelineBenchmarks {
    readonly Random _rng = new(42);

    ClientBenchmarkFixture _fixture  = null!;
    KurrentClient          _client   = null!;
    StreamsClient          _streams  = null!;
    WriteRecordPipeline    _pipeline = null!;
    WriteRecordPipeline    _split    = null!;
    AppendRecord           _record   = null!;

    /// <summary>bytes: 64-byte raw payload (envelope floor). json and proto: the SAME
    /// <c>AppendRecordsRequest</c> instance via System.Text.Json and IMessage.ToByteArray. msgpack
    /// and msgpack-nerdbank: a same-shape POCO mirror (see class doc) via MessagePack-CSharp and
    /// Nerdbank.MessagePack respectively.</summary>
    [Params("bytes", "json", "proto", "msgpack", "msgpack-nerdbank")]
    public string Payload { get; set; } = "";

    [GlobalSetup]
    public async Task Setup() {
        _fixture = new ClientBenchmarkFixture();
        await _fixture.As<IAsyncInitializer>().InitializeAsync();

        // Own client: catalogs freeze at construction, and the shared payload type needs a mapping.
        _client = KurrentClient.Create(_fixture.ConnectionString, opts => {
            opts.LoggerFactory = _fixture.LoggerFactory;
            opts.Schema        = KurrentClientSchemaOptions.NoValidation;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.UseCatalog(new MessageCatalog("mapping-bench").Map<Contracts.AppendRecordsRequest>());

            // MessagePackSchemaSerializer.DataFormat is the same SchemaDataFormat.MessagePack value
            // for both libraries, and ISchemaSerializerProvider keys one serializer per format — so
            // only the active case's serializer is registered, never both at once. Each gets its own
            // standalone SchemaManager (no registry, ManualMap only), same construction as the two
            // packages' own unit tests.
            switch (Payload) {
                case "msgpack":
                    var officialCatalog = new MessageCatalog("mapping-bench-msgpack").Map<CalibrationMsgPackRequest>();

                    opts.Messages.UseSerializer(_ => new OfficialMsgPack.MessagePackSchemaSerializer(
                        new OfficialMsgPack.MessagePackSchemaSerializerOptions {
                            SchemaRegistration = SchemaRegistrationOptions.ManualMap,
                            UserResolvers      = [CalibrationMessagePackResolver.Instance]
                        },
                        new SchemaManager(null!, null!, MessageResolver.From(officialCatalog))
                    ));
                    break;

                case "msgpack-nerdbank":
                    var nerdbankCatalog = new MessageCatalog("mapping-bench-msgpack-nerdbank").Map<CalibrationMsgPackRequest>();

                    opts.Messages.UseSerializer(_ => new NerdbankMsgPack.MessagePackSchemaSerializer(
                        new NerdbankMsgPack.MessagePackSchemaSerializerOptions {
                            SchemaRegistration = SchemaRegistrationOptions.ManualMap,
                            TypeShapeProvider  = CalibrationMessagePackWitness.GeneratedTypeShapeProvider
                        },
                        new SchemaManager(null!, null!, MessageResolver.From(nerdbankCatalog))
                    ));
                    break;
            }
        });

        _streams = new StreamsClient(_client);

        var mapping = _streams.CreateMappingContext(CancellationToken.None);
        _pipeline = new WriteRecordPipeline(mapping.SerializerProvider, mapping.Policy);
        _split    = WriteRecordPipeline.Split(mapping.SerializerProvider, mapping.Policy);

        var request = NewAppendRequest();

        _record = Payload switch {
            "json"  => AppendRecord.Create(request, r => r.Stream = "write-pipeline-bench"),

            "proto" => AppendRecord.Create(request, r => {
                r.Stream     = "write-pipeline-bench";
                r.DataFormat = SchemaDataFormat.Protobuf;
            }),

            "msgpack" or "msgpack-nerdbank" => AppendRecord.Create(NewMsgPackRequest(), r => {
                r.Stream     = "write-pipeline-bench";
                r.DataFormat = SchemaDataFormat.MessagePack;
            }),

            _ => AppendRecord.Create(RandomBytes(), r => {
                r.Stream     = "write-pipeline-bench";
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
                Stream   = "write-pipeline-bench",
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

    // The msgpack-safe mirror of NewAppendRequest — same record count, field shape, and
    // deterministic values, on a benchmark-owned type instead of the protobuf wire contract.
    static CalibrationMsgPackRequest NewMsgPackRequest() {
        var records = new List<CalibrationMsgPackRecord>();

        for (var i = 0; i < 2; i++) {
            records.Add(new CalibrationMsgPackRecord {
                RecordId   = $"00000000-0000-0000-0000-0000000000{i:00}",
                Stream     = "write-pipeline-bench",
                Data       = """{"value":1,"name":"calibration"}""",
                SchemaId   = $"00000000-0000-0000-0000-0000000001{i:00}",
                SchemaName = "calibration-event"
            });
        }

        return new CalibrationMsgPackRequest { Records = records };
    }

    [Benchmark(Baseline = true, Description = "ToContract mapping")]
    public async ValueTask<object> to_contract() =>
        await _record.ToContract(_streams.CreateMappingContext(CancellationToken.None));

    [Benchmark(Description = "WriteRecordPipeline (fused encode)")]
    public async ValueTask<object> pipeline() =>
        await _pipeline.Execute(_record, CancellationToken.None);

    [Benchmark(Description = "WriteRecordPipeline.Split (resolve + encode)")]
    public async ValueTask<object> split_pipeline() =>
        await _split.Execute(_record, CancellationToken.None);

    [GlobalCleanup]
    public async Task Cleanup() {
        await _client.DisposeAsync();
        await _fixture.As<IAsyncDisposable>().DisposeAsync();
    }
}
