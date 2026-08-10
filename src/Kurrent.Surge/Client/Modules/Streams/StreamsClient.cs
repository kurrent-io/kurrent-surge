using Kurrent.Surge.Client.Schema;
using LegacyStreamsServiceClient = KurrentDB.Protocol.V1.Streams.Streams.StreamsClient;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace Kurrent.Surge.Client.Streams;

public partial class StreamsClient : ClientModuleBase, IStreamsClient {
    internal StreamsClient(KurrentClient client) : base(client) {
        // System mappings ($metadata, $>, scavenge events) live in SystemMessageCatalog:
        // the resolver snapshot is taken at client construction, before modules exist.
        ServiceClient       = new(client.CallInvoker);
        LegacyServiceClient = new(client.CallInvoker);

        // Soon enough, the schema registry policy will be retrieved from the server during client initialization (defaults),
        // and then on a per-stream basis during stream operations.
        // For now, we can use a default policy that allows for all operations without enforcement.
        RegistryPolicy = SchemaRegistryPolicy.NoRequirements;

        // One decoder per client: it is stateless and the policy is fixed at construction,
        // so every mapped record shares it instead of allocating its own.
        RecordDecoder = new RecordDecoder(SerializerProvider, RegistryPolicy);
    }

    internal StreamsServiceClient ServiceClient { get; }

    internal LegacyStreamsServiceClient LegacyServiceClient { get; }

    internal SchemaRegistryPolicy RegistryPolicy { get; }

    internal IRecordDecoder RecordDecoder { get; }

    internal MappingContext CreateMappingContext(CancellationToken ct) =>
        new(SerializerProvider, MetadataDecoder, RegistryPolicy, RecordDecoder, ct);
}
