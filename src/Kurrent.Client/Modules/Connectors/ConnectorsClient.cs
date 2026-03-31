using Grpc.Core;
using Kurrent.Client.Exceptions;

using static KurrentDB.Connectors.Management.Contracts.Commands.ConnectorsCommandService;
using static KurrentDB.Connectors.Management.Contracts.Queries.ConnectorsQueryService;

namespace Kurrent.Client.Connectors;

public partial class ConnectorsClient : ClientModuleBase {
    internal ConnectorsClient(KurrentClient client) : base(client) {
        CommandServiceClient = new(client.LegacyCallInvoker);
        QueryServiceClient   = new(client.LegacyCallInvoker);
    }

    ConnectorsCommandServiceClient CommandServiceClient { get; }
    ConnectorsQueryServiceClient   QueryServiceClient   { get; }

    // The server does not yet send ErrorInfo. Remove when the server sends structured error details
    static Exception ToConnectorException(
        Exception ex,
        ConnectorNotFoundException notFound,
        ConnectorDeletedException deleted
    ) {
        var mex = ex.ToException(
            ConnectorNotFoundException.From,
            ConnectorDeletedException.From);

        if (mex is ConnectorNotFoundException or ConnectorDeletedException)
            return mex;

        return ex switch {
            RpcException { StatusCode: StatusCode.NotFound }           => notFound,
            RpcException { StatusCode: StatusCode.FailedPrecondition } => deleted,
            _                                                          => mex
        };
    }
}
