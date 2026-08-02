namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Owns one projection's client and transaction lifetime, across as many batches as run between
/// checkpoints.
/// </summary>
/// <typeparam name="TClient">The backend's client type — a connection, or whatever else a backend writes through.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Open"/> is idempotent while a transaction is open: the first call after construction, or
/// after a <see cref="Commit"/>, opens a fresh transaction; every call in between returns the same
/// client rather than opening another one on top of it — most backends, DuckDB included, have no nested
/// transactions.
/// </para>
/// <para>
/// There is no rollback. A projection never discards completed work — the checkpoint commits whatever
/// the tracker claims. The one genuine discard is the death of the run itself, and the implementation's
/// disposal rolls back anything that was never committed.
/// </para>
/// </remarks>
[PublicAPI]
public interface IProjectionTransaction<TClient> {
    /// <summary>Opens a transaction if none is open, and returns the client it is open on.</summary>
    ValueTask<TClient> Open(CancellationToken cancellationToken);

    /// <summary>Commits the open transaction.</summary>
    ValueTask Commit(CancellationToken cancellationToken);
}
