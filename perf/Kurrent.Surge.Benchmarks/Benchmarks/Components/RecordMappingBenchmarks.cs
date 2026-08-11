using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Streams.Checkpoints;
using Kurrent.Surge.Schema;

using ReadEvent = KurrentDB.Protocol.V1.Streams.ReadResp.Types.ReadEvent;

namespace Benchmarks;

/// <summary>
/// Isolates the wire→Record mapping step from the network: pre-built ReadEvents mapped
/// in memory, no server. Two decoder shapes run side by side — a fresh RecordDecoder per
/// record (the pre-hoist shape) against the client's shared instance — so the per-record
/// cost of the decoder allocation is a within-run comparison immune to transport noise.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Mapping")]
public class RecordMappingBenchmarks {
    const int RecordsPerOp = 10_000;

    static readonly ByteString Payload = ByteString.CopyFromUtf8("{\"value\":1}");

    readonly DefaultMetadataDecoder _metadataDecoder = new();

    ReadEvent[]    _events        = null!;
    RecordDecoder  _sharedDecoder = null!;

    [GlobalSetup]
    public void Setup() {
        _sharedDecoder = new RecordDecoder(NullSerializerProvider.Instance, SchemaRegistryPolicy.NoRequirements);

        _events = new ReadEvent[RecordsPerOp];
        for (var i = 0; i < RecordsPerOp; i++)
            _events[i] = NewReadEvent(i);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = RecordsPerOp, Description = "decoder per record (pre-hoist shape)")]
    public async Task<int> map_with_decoder_per_record() {
        var count = 0;

        foreach (var readEvent in _events) {
            var ctx = new MappingContext(
                NullSerializerProvider.Instance,
                _metadataDecoder,
                SchemaRegistryPolicy.NoRequirements,
                new RecordDecoder(NullSerializerProvider.Instance, SchemaRegistryPolicy.NoRequirements),
                CancellationToken.None);

            var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);
            count += record.IsLink ? 0 : 1;
        }

        return count;
    }

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "shared decoder (hoisted)")]
    public async Task<int> map_with_shared_decoder() {
        var ctx = new MappingContext(
            NullSerializerProvider.Instance,
            _metadataDecoder,
            SchemaRegistryPolicy.NoRequirements,
            _sharedDecoder,
            CancellationToken.None);

        var count = 0;

        foreach (var readEvent in _events) {
            var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);
            count += record.IsLink ? 0 : 1;
        }

        return count;
    }

    // Deterministic hand-built events — Bogus randomness would vary string lengths between
    // runs and pollute the allocation comparison. Shape mirrors the V1 wire contract: schema
    // info lives in the metadata map ("type", "content-type"), "created" is ticks-since-epoch.
    static ReadEvent NewReadEvent(int index) {
        var recorded = new ReadEvent.Types.RecordedEvent {
            Id               = Uuid.FromGuid(new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ToDto(),
            StreamIdentifier = "record-mapping-bench",
            StreamRevision   = (ulong)index,
            CommitPosition   = (ulong)(index + 1000),
            Data             = Payload,
            CustomMetadata   = ByteString.Empty
        };

        recorded.Metadata["type"]         = "TestEvent";
        recorded.Metadata["created"]      = "1754000000000000";
        recorded.Metadata["content-type"] = "application/json";

        return new ReadEvent { Event = recorded, CommitPosition = recorded.CommitPosition };
    }

    sealed class NullSerializerProvider : ISchemaSerializerProvider {
        public static readonly NullSerializerProvider Instance = new();

        public bool SupportsDataFormat(SchemaDataFormat dataFormat) => false;

        public ISchemaSerializer GetSerializer(SchemaDataFormat dataFormat) =>
            throw new NotSupportedException();
    }
}
