using LegacyStreamsServiceClient = KurrentDB.Protocol.V1.Streams.Streams.StreamsClient;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace Kurrent.Client.Streams;

public partial class StreamsClient : ClientModuleBase {
    internal StreamsClient(KurrentClient client) : base(client) {
        TypeMapper.Map<StreamMetadata>("$metadata"); // metastream
        TypeMapper.Map<string>("$>");                // link

        ServiceClient       = new StreamsServiceClient(client.LegacyCallInvoker);
        LegacyServiceClient = new LegacyStreamsServiceClient(client.LegacyCallInvoker);
    }

    internal StreamsServiceClient       ServiceClient       { get; }
    internal LegacyStreamsServiceClient LegacyServiceClient { get; }
}
