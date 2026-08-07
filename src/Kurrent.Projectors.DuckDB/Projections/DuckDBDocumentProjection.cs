using System.Text.Json;

using Kurrent.Projectors.Transactional;
using Kurrent.Quack;
using Kurrent.Surge.Streaming;

namespace Kurrent.Projectors.DuckDB;

[PublicAPI]
public abstract class DuckDBDocumentProjection<TDocument, TKey> : DocumentProjection<DuckDBAdvancedConnection, TDocument, TKey>
    where TDocument : class
    where TKey : notnull {
    protected DuckDBDocumentProjection(TimeProvider? time = null, JsonSerializerOptions? json = null)
        : this(new DuckDBDocumentStore(time, json)) { }

    DuckDBDocumentProjection(DuckDBDocumentStore store) : base(store) {
        _store = store;

        Setup((_, cancellationToken) => store.EnsureCollection<TDocument>(cancellationToken));
    }

    readonly DuckDBDocumentStore _store;

    DuckDBProjectionTransaction                          _transaction = null!;
    IProjectionCheckpointStore<DuckDBAdvancedConnection> _checkpoints = null!;

    public override void Wire(
        string projectionId,
        IProjectionClientProvider<DuckDBAdvancedConnection> clients,
        IProjectionCheckpointStore<DuckDBAdvancedConnection> checkpoints
    ) {
        base.Wire(projectionId, clients, checkpoints);

        if (clients is not IDuckDBConnectionProvider provider)
            throw new InvalidOperationException(
                $"{nameof(DuckDBDocumentProjection<TDocument, TKey>)} needs an "
              + $"{nameof(IDuckDBConnectionProvider)}, but was given {clients.GetType().Name}.");

        _checkpoints = checkpoints;
        _transaction = new DuckDBProjectionTransaction(provider);

        _store.Bind(_transaction);
    }

    protected override async ValueTask OnCheckpoint(CheckpointContext context) {
        await base.OnCheckpoint(context).ConfigureAwait(false);

        var connection = await _transaction.Open(context.CancellationToken).ConfigureAwait(false);

        await _checkpoints
            .Store(connection, context.Positions, context.CancellationToken)
            .ConfigureAwait(false);

        await _transaction.Commit(context.CancellationToken).ConfigureAwait(false);
    }
}
