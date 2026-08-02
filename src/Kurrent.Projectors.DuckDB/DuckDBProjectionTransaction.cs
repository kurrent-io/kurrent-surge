using Kurrent.Projectors.Transactional;
using Kurrent.Quack;

namespace Kurrent.Projectors.DuckDB;

/// <summary>
/// The DuckDB backend's <see cref="IProjectionTransaction{TClient}"/>: one connection, held for the
/// projection's lifetime, and one transaction, opened once and left open across every batch until
/// <see cref="Commit"/> closes it.
/// </summary>
/// <remarks>
/// <para>
/// The connection is borrowed through <see cref="IDuckDBConnectionProvider.GetScopedConnection"/> and
/// released by disposing that handle, never the connection itself. The two providers need opposite things
/// — a pool rental has to go back, and the single shared in-memory connection must survive every borrower —
/// and the handle is what makes both correct from one call site.
/// </para>
/// <para>
/// <see cref="DuckDBAdvancedConnection.BeginTransaction"/> hides the base connection's own method and
/// returns Quack's <see cref="TransactionScope"/> instead of <see cref="DuckDB.NET.Data.DuckDBTransaction"/>
/// — a <c>struct</c> with no immediate <c>Commit</c>/<c>Rollback</c> calls; it commits on disposal only if
/// <see cref="TransactionScope.CommitOnDispose"/> was called first, and rolls back otherwise.
/// <see cref="TransactionScope"/> implements <see cref="IDisposable"/> explicitly, so it must be disposed
/// through that interface rather than a direct <c>.Dispose()</c> call. Because it is a value type, it is
/// held in a plain (non-nullable) field rather than a <c>TransactionScope?</c> — <c>.Value</c> on a
/// nullable struct copies, and <see cref="TransactionScope.CommitOnDispose"/> mutating that copy would be
/// silently lost; every call here goes through the field directly so the same instance is read back by
/// <see cref="Commit"/>'s <c>Dispose</c>. A separate <c>_isOpen</c> flag stands in for the null check the
/// struct cannot carry.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DuckDBProjectionTransaction(IDuckDBConnectionProvider provider)
    : IProjectionTransaction<DuckDBAdvancedConnection>, IAsyncDisposable {
    DuckDBAdvancedConnection? _connection;
    IDisposable?              _rental;
    TransactionScope          _transaction;
    bool                      _isOpen;

    /// <inheritdoc/>
    public ValueTask<DuckDBAdvancedConnection> Open(CancellationToken cancellationToken) {
        // Both guards are the contract: ProjectionProcessor calls Open once per batch and again at the
        // checkpoint, and one transaction spans every batch in between. Re-opening here would strand
        // each batch's writes in a superseded transaction, and DuckDB has no nested transactions.
        if (_connection is null) {
            _rental     = provider.GetScopedConnection(out var connection);
            _connection = connection;
        }

        if (!_isOpen) {
            _transaction = _connection.BeginTransaction();
            _isOpen      = true;
        }

        return ValueTask.FromResult(_connection);
    }

    /// <inheritdoc/>
    public ValueTask Commit(CancellationToken cancellationToken) {
        if (!_isOpen)
            throw new InvalidOperationException("No transaction is open. Call Open() first.");

        _transaction.CommitOnDispose();
        Close();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns the borrowed connection. A transaction still open was never committed, and disposing
    /// its scope uncommitted rolls it back — the run died before its checkpoint.
    /// </summary>
    public ValueTask DisposeAsync() {
        if (_isOpen)
            Close();

        _rental?.Dispose();

        _rental     = null;
        _connection = null;

        return ValueTask.CompletedTask;
    }

    void Close() {
        ((IDisposable)_transaction).Dispose();

        _transaction = default;
        _isOpen      = false;
    }
}
