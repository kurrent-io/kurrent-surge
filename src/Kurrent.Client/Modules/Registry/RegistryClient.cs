using static KurrentDB.Protocol.V2.Registry.SchemaRegistryService;

namespace Kurrent.Client.Registry;

public partial class RegistryClient : ClientModuleBase, IRegistryClient {
    internal RegistryClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.LegacyCallInvoker);

    SchemaRegistryServiceClient ServiceClient { get; }
}
