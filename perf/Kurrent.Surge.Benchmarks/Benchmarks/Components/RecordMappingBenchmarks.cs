using BenchmarkDotNet.Attributes;
using Google.Protobuf;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Streams.Checkpoints;
using Kurrent.Surge.Client.Legacy;
using Kurrent.Surge.Schema;

using ReadEvent = KurrentDB.Protocol.V1.Streams.ReadResp.Types.ReadEvent;

namespace Benchmarks;

/// <summary>
/// Isolates the wire→Record mapping step from the network: pre-built ReadEvents mapped in
/// memory, no server round trip. The decoder is the client's real ReadRecordPipeline, built
/// from client-composed parts — KurrentClient construction is lazy (no connection is dialed
/// until the first RPC), so this stays a pure in-memory measurement. End-to-end append/read
/// benchmarks cannot resolve mapping-stage deltas on their own; this rig is what does.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("Mapping")]
public class RecordMappingBenchmarks {
    const int RecordsPerOp = 10_000;

    static readonly ByteString Payload = ByteString.CopyFromUtf8("{\"value\":1}");

    readonly DefaultMetadataDecoder _metadataDecoder = new();

    KurrentClient  _client  = null!;
    ReadEvent[]    _events  = null!;
    IRecordDecoder _decoder = null!;

    [GlobalSetup]
    public void Setup() {
        // No connection is dialed here — KurrentClient construction is lazy.
        _client  = KurrentClient.Create("kurrentdb://localhost:2113?tls=false");
        _decoder = _client.ReadPipeline;

        _events = new ReadEvent[RecordsPerOp];
        for (var i = 0; i < RecordsPerOp; i++)
            _events[i] = NewReadEvent(i);
    }

    [Benchmark(OperationsPerInvoke = RecordsPerOp, Description = "wire-to-Record mapping (shared ReadRecordPipeline)")]
    public async Task<int> map_records() {
        var ctx = new MappingContext(_metadataDecoder, _decoder, CancellationToken.None);

        var count = 0;

        foreach (var readEvent in _events) {
            var record = await readEvent.MapToRecordAsync(SequenceId.From(1), ctx, skipDecoding: true);
            count += record.IsLink ? 0 : 1;
        }

        return count;
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _client.DisposeAsync();

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
}
