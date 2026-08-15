using Kurrent.Surge.Client.Schema;
using LegacyStreamsServiceClient = KurrentDB.Protocol.V1.Streams.Streams.StreamsClient;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace Kurrent.Surge.Client.Streams;

public partial class StreamsClient : ClientModuleBase, IStreamsClient {
    internal StreamsClient(KurrentClient client) : base(client) {
        // System mappings ($metadata, $>, scavenge events) live in SystemMessageCatalog:
        // the resolver snapshot is taken at client construction, before modules exist.
        ServiceClient           = new(client.CallInvoker);
        LegacyServiceClient     = new(client.CallInvoker);
        LegacyReadServiceClient = new(client.ReadCallInvoker);

        RegistryPolicy = client.RegistryPolicy;

        // One pipeline per client: stateless, policy fixed at construction, so every mapped
        // record shares it instead of allocating its own.
        RecordDecoder = client.ReadPipeline;
        WritePipeline = client.WritePipeline;
    }

    internal StreamsServiceClient ServiceClient { get; }

    internal LegacyStreamsServiceClient LegacyServiceClient { get; }

    internal LegacyStreamsServiceClient LegacyReadServiceClient { get; }

    internal SchemaRegistryPolicy RegistryPolicy { get; }

    internal IRecordDecoder RecordDecoder { get; }

    internal WriteRecordPipeline WritePipeline { get; }

    internal MappingContext CreateMappingContext(CancellationToken ct) =>
        new(MetadataDecoder, RecordDecoder, ct);
}
