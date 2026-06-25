using static KurrentDB.Connectors.Management.Contracts.Commands.ConnectorsCommandService;
using static KurrentDB.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

namespace Kurrent.Client.Connectors;

public partial class ConnectorsClient : ClientModuleBase, IConnectorsClient {
    internal ConnectorsClient(KurrentClient client) : base(client) {
        CommandServiceClient = new(client.LegacyCallInvoker);
        QueryServiceClient   = new(client.LegacyCallInvoker);
    }

    ConnectorsCommandServiceClient CommandServiceClient { get; }
    ConnectorsQueryServiceClient   QueryServiceClient   { get; }
}
