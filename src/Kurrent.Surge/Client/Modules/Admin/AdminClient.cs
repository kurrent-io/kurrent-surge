using Kurrent.Surge.Client.Exceptions;
using KurrentDB.Protocol.V1;
using OperationsServiceClient = KurrentDB.Protocol.V1.Operations.Operations.OperationsClient;

namespace Kurrent.Surge.Client.Admin;

public partial class AdminClient : IAdminClient {
    internal AdminClient(KurrentClient client) {
        Client             = client;
        AdminServiceClient = new(client.CallInvoker);
    }

    KurrentClient Client { get; }

    OperationsServiceClient AdminServiceClient { get; }

    static readonly Empty EmptyRequest = new();

    public async ValueTask ShutdownServer(CancellationToken cancellationToken = default) {
        try {
            await AdminServiceClient
                .ShutdownAsync(EmptyRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public async ValueTask ResignNode(CancellationToken cancellationToken = default) {
        try {
            await AdminServiceClient
                .ResignNodeAsync(EmptyRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public async ValueTask SetNodePriority(int nodePriority, CancellationToken cancellationToken = default) {
        ArgumentOutOfRangeException.ThrowIfNegative(nodePriority);

        try {
            await AdminServiceClient
                .SetNodePriorityAsync(new() { Priority = nodePriority }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }
}
