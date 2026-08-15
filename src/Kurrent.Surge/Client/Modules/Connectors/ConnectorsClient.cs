using static KurrentDB.Connectors.Management.Contracts.Commands.ConnectorsCommandService;
using static KurrentDB.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

namespace Kurrent.Surge.Client.Connectors;

public partial class ConnectorsClient : ClientModuleBase, IConnectorsClient {
    internal ConnectorsClient(KurrentClient client) : base(client) {
        CommandServiceClient = new(client.CallInvoker);
        QueryServiceClient   = new(client.CallInvoker);
    }

    ConnectorsCommandServiceClient CommandServiceClient { get; }
    ConnectorsQueryServiceClient   QueryServiceClient   { get; }
}
