using static KurrentDB.Protocol.V2.Registry.SchemaRegistryService;

namespace Kurrent.Surge.Client.Registry;

public sealed partial class RegistryClient : ClientModuleBase, IRegistryClient {
    internal RegistryClient(KurrentClient client) : base(client) =>
        ServiceClient = new(client.CallInvoker);

    SchemaRegistryServiceClient ServiceClient { get; }
}
