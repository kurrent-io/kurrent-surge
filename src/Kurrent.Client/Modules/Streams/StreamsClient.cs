using Kurrent.Client.Schema;
using LegacyStreamsServiceClient = KurrentDB.Protocol.V1.Streams.Streams.StreamsClient;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace Kurrent.Client.Streams;

public partial class StreamsClient : ClientModuleBase {
    internal StreamsClient(KurrentClient client) : base(client) {
        TypeMapper.Map<StreamMetadata>("$metadata"); // metastream
        TypeMapper.Map<string>("$>");                // link
        TypeMapper.MapScavengeEvents();

        ServiceClient       = new(client.LegacyCallInvoker);
        LegacyServiceClient = new(client.LegacyCallInvoker);

        // Soon enough, the schema registry policy will be retrieved from the server during client initialization (defaults),
        // and then on a per-stream basis during stream operations.
        // For now, we can use a default policy that allows for all operations without enforcement.
        Mapper = new(SerializerProvider, SchemaRegistryPolicy.NoRequirements);
    }

    internal StreamsServiceClient       ServiceClient       { get; }

    internal LegacyStreamsServiceClient LegacyServiceClient { get; }

    internal StreamsProtocolMapper      Mapper              { get; }
}
