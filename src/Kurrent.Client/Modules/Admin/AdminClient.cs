using Kurrent.Client.Exceptions;
using KurrentDB.Protocol.V1;
using KurrentDB.Protocol.V1.Operations;
using OperationsServiceClient = KurrentDB.Protocol.V1.Operations.Operations.OperationsClient;

namespace Kurrent.Client.Admin;

public partial class AdminClient {
    internal AdminClient(KurrentClient source) {
        Source             = source;
        AdminServiceClient = new OperationsServiceClient(source.LegacyCallInvoker);
    }

    KurrentClient Source { get; }

    OperationsServiceClient AdminServiceClient { get; }

    static readonly Empty EmptyRequest = new();

    public async ValueTask ShutdownServer(CancellationToken cancellationToken = default) {
        try {
            await AdminServiceClient
                .ShutdownAsync(EmptyRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.ToException();
        }
    }

    public async ValueTask ResignNode(CancellationToken cancellationToken = default) {
        try {
            await AdminServiceClient
                .ResignNodeAsync(EmptyRequest, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.ToException();
        }
    }

    public async ValueTask SetNodePriority(int nodePriority, CancellationToken cancellationToken = default) {
        ArgumentOutOfRangeException.ThrowIfNegative(nodePriority);

        try {
            await AdminServiceClient
                .SetNodePriorityAsync(new SetNodePriorityReq { Priority = nodePriority }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            throw ex.ToException();
        }
    }
}
