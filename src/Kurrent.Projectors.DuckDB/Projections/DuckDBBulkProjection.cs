using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public abstract class DuckDBBulkProjection : BulkProjection<DuckDBAdvancedConnection> {
    protected DuckDBBulkProjection() => Project(Project);

    IProjectionCheckpointStore<DuckDBAdvancedConnection> _checkpoints = null!;

    public abstract ValueTask Project(BulkProjectionContext<DuckDBAdvancedConnection> context);

    protected DuckDBProjectionTransaction Transaction { get; private set; } = null!;

    protected ValueTask<DuckDBAdvancedConnection> Connection(CancellationToken cancellationToken) =>
        Transaction.Open(cancellationToken);

    public override void Wire(
        string projectionId,
        IProjectionClientProvider<DuckDBAdvancedConnection> clients,
        IProjectionCheckpointStore<DuckDBAdvancedConnection> checkpoints
    ) {
        base.Wire(projectionId, clients, checkpoints);

        if (clients is not IDuckDBConnectionProvider provider)
            throw new InvalidOperationException(
                $"{GetType().Name} needs an {nameof(IDuckDBConnectionProvider)}, but was given "
              + $"{clients.GetType().Name}.");

        _checkpoints = checkpoints;
        Transaction  = new DuckDBProjectionTransaction(provider);
    }

    protected override async ValueTask OnCheckpoint(CheckpointContext context) {
        var connection = await Transaction.Open(context.CancellationToken).ConfigureAwait(false);

        await _checkpoints
            .Store(connection, context.Positions, context.CancellationToken)
            .ConfigureAwait(false);

        await Transaction.Commit(context.CancellationToken).ConfigureAwait(false);
    }
}
